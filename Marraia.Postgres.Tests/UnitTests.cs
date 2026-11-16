global using static Marraia.Postgres.Tests.TestHelpers;
using System.Data;
using Marraia.Postgres.Comum;
using Marraia.Postgres.Configurations;
using Marraia.Postgres.Repositories.Interfaces;
using Marraia.Postgres.Uow.Transactions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Marraia.Postgres.Tests;

public class SqlGenerationTests
{
    private sealed class Sql<T> : CommonConfiguration<T> where T : class
    {
        public Sql(IConfiguration? configuration) : base(configuration) { }
        public string Update => GenerateUpdateQuery();
        public string UpdateOnly(params string[] columns) => GenerateUpdateQuery(columns);
    }

    private static IConfiguration Config(string? schema) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(schema is null ? [] : new Dictionary<string, string?> { ["SchemaBD"] = schema })
            .Build();

    [Fact]
    public void Entity_without_Description_uses_class_name()
    {
        var sql = new Sql<Person>(Config("public"));
        Assert.Equal("SELECT * FROM public.Person WHERE Id=@Id", sql.GenerateSelectByIdQuery());
    }

    [Fact]
    public void Missing_schema_does_not_generate_leading_dot()
    {
        var sql = new Sql<Person>(Config(null));
        Assert.Equal("SELECT * FROM Person", sql.GenerateSelectAllQuery());
        Assert.Equal("DELETE FROM Person WHERE Id=@Id", sql.GenerateDeleteQuery());
    }

    [Fact]
    public void Insert_with_serial_key_omits_Id_and_uses_RETURNING()
    {
        var sql = new Sql<Person>(Config("app"));
        Assert.Equal(
            "INSERT INTO app.Person (Name,Surname) VALUES (@Name,@Surname) RETURNING Id",
            sql.GenerateInsertQuery());
    }

    [Fact]
    public void Insert_with_guid_key_includes_Id_and_skips_Description_property()
    {
        var sql = new Sql<Product>(Config(null));
        Assert.Equal(
            "INSERT INTO product (Title,Id) VALUES (@Title,@Id) RETURNING Id",
            sql.GenerateInsertQuery());
    }

    [Fact]
    public void Update_skips_Id_and_ignored_properties()
    {
        var sql = new Sql<Person>(Config("app"));
        Assert.Equal("UPDATE app.Person SET Name=@Name,Surname=@Surname WHERE Id=@Id", sql.Update);
        Assert.Equal("UPDATE app.Person SET Name=@Name WHERE Id=@Id", sql.UpdateOnly("Id", "Name"));
    }

    [Fact]
    public void Statements_are_cached_per_schema()
    {
        var a = new Sql<Person>(Config("s1"));
        var b = new Sql<Person>(Config("s2"));
        Assert.Contains("s1.Person", a.GenerateSelectAllQuery());
        Assert.Contains("s2.Person", b.GenerateSelectAllQuery());
        Assert.Same(a.GenerateInsertQuery(), new Sql<Person>(Config("s1")).GenerateInsertQuery());
    }
}

public class EntityTests
{
    [Fact]
    public void Same_type_and_id_are_equal()
    {
        var a = new Person("a", "b"); a.SetId(1);
        var b = new Person("c", "d"); b.SetId(1);
        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void Transient_entities_are_not_equal()
    {
        Assert.NotEqual(new Person("a", "b"), new Person("a", "b"));
    }
}

public class DependencyInjectionTests
{
    [Fact]
    public void AddPostgres_registers_services()
    {
        var services = new ServiceCollection().AddPostgres("Host=localhost;Database=x;Username=u;Password=p");
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<PostgresConnectionFactory>(scope.ServiceProvider.GetRequiredService<IPostgresConnectionFactory>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPostgresUnitOfWork>());
        Assert.Same(
            scope.ServiceProvider.GetRequiredService<IPostgresTransactionBase>(),
            scope.ServiceProvider.GetRequiredService<IPostgresTransactionBase>());
    }

    [Fact]
    public void AddPostgres_twice_throws_instead_of_silently_overriding()
    {
        var services = new ServiceCollection().AddPostgres("Host=a");
        Assert.Throws<InvalidOperationException>(() => services.AddPostgres("Host=b"));
    }

    [Fact]
    public void AddPostgres_with_empty_connection_string_throws()
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddPostgres(" "));
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddPostgres(" ", "Host=w"));
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddPostgres("Host=r", " "));
    }

    [Fact]
    public void Single_connection_mode_does_not_register_read_write_factories()
    {
        using var provider = new ServiceCollection().AddPostgres("Host=primary").BuildServiceProvider();

        Assert.Null(provider.GetService<IPostgresReadConnectionFactory>());
        Assert.Null(provider.GetService<IPostgresWriteConnectionFactory>());
    }

    [Fact]
    public void Segregated_mode_registers_read_write_and_default_uses_write()
    {
        var services = new ServiceCollection().AddPostgres(
            connectionStringRead: "Host=replica;Database=db",
            connectionStringWrite: "Host=primary;Database=db");
        using var provider = services.BuildServiceProvider();

        var read = provider.GetRequiredService<IPostgresReadConnectionFactory>();
        var write = provider.GetRequiredService<IPostgresWriteConnectionFactory>();
        var single = provider.GetRequiredService<IPostgresConnectionFactory>();

        Assert.Equal("replica", HostOf(read.CreateConnection()));
        Assert.Equal("primary", HostOf(write.CreateConnection()));
        // Repositórios no modo padrão continuam funcionando, usando o banco de escrita.
        Assert.Same(write, single);
    }

    [Fact]
    public async Task Segregated_mode_disposes_shared_write_factory_safely()
    {
        var provider = new ServiceCollection().AddPostgres("Host=r", "Host=w").BuildServiceProvider();
        provider.GetRequiredService<IPostgresConnectionFactory>();
        provider.GetRequiredService<IPostgresReadConnectionFactory>();

        await provider.DisposeAsync(); // mesma instância exposta por duas interfaces: não pode falhar
    }
}

public class TransactionBaseTests
{
    private sealed class FakeTransaction : IDbTransaction
    {
        public IDbConnection? Connection => null;
        public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        public void Commit() { }
        public void Rollback() { }
        public void Dispose() { }
    }

    private static TransactionBase WithActiveTransaction()
    {
        var tb = new TransactionBase();
        tb.AddTransaction(new FakeTransaction(), new NpgsqlConnection());
        return tb;
    }

    [Fact]
    public async Task Without_transaction_reports_not_executed()
    {
        var (executed, _) = await new TransactionBase()
            .TryExecuteInTransactionAsync((_, _) => Task.FromResult(1));
        Assert.False(executed);
    }

    [Fact]
    public async Task Parallel_operations_in_transaction_are_serialized()
    {
        var tb = WithActiveTransaction();
        var current = 0;
        var max = 0;

        var tasks = Enumerable.Range(0, 20).Select(_ => tb.TryExecuteInTransactionAsync(async (_, _) =>
        {
            var now = Interlocked.Increment(ref current);
            InterlockedMax(ref max, now);
            await Task.Delay(5);
            Interlocked.Decrement(ref current);
            return now;
        }));

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.True(r.Executed));
        Assert.Equal(1, max);
    }

    [Fact]
    public async Task Nested_calls_in_same_flow_do_not_deadlock()
    {
        var tb = WithActiveTransaction();

        var outer = tb.TryExecuteInTransactionAsync(async (_, _) =>
        {
            var (innerExecuted, value) = await tb.TryExecuteInTransactionAsync((_, _) => Task.FromResult(42));
            Assert.True(innerExecuted);
            return value;
        });

        var finished = await Task.WhenAny(outer, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(outer, finished);
        Assert.Equal(42, (await outer).Result);
    }

    [Fact]
    public async Task Cleared_while_waiting_falls_back_to_not_executed()
    {
        var tb = WithActiveTransaction();
        var release = new TaskCompletionSource();

        var first = tb.TryExecuteInTransactionAsync(async (_, _) => { await release.Task; return 1; });
        var second = tb.TryExecuteInTransactionAsync((_, _) => Task.FromResult(2));

        tb.Clear();
        release.SetResult();

        Assert.True((await first).Executed);
        Assert.False((await second).Executed);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int snapshot;
        while ((snapshot = Volatile.Read(ref target)) < value &&
               Interlocked.CompareExchange(ref target, value, snapshot) != snapshot) { }
    }
}

public class UnitOfWorkTests
{
    [Fact]
    public async Task Commit_and_Rollback_without_transaction_are_safe()
    {
        var factory = new PostgresConnectionFactory("Host=localhost");
        var uow = new PostgresUnitOfWork(factory, new PostgresTransactionBase());

        Assert.False(uow.Commit());
        Assert.False(await uow.CommitAsync());
        uow.Rollback();
        await uow.RollbackAsync();
        uow.Dispose();
        await uow.DisposeAsync();
    }
}

internal static class TestHelpers
{
    public static string? HostOf(Npgsql.NpgsqlConnection connection)
        => new Npgsql.NpgsqlConnectionStringBuilder(connection.ConnectionString).Host;
}

public class DirectConnectionTests
{
    private static IConfiguration EmptyConfig() => new ConfigurationBuilder().Build();

    [Fact]
    public void Without_transaction_returns_closed_reusable_connection()
    {
        var factory = new PostgresConnectionFactory("Host=primary");
        var repo = new PersonQueries(factory, new PostgresTransactionBase(), EmptyConfig());

        var conn = repo.DirectConnection;

        Assert.Equal(ConnectionState.Closed, conn.State); // Dapper abre/fecha a cada comando
        Assert.Same(conn, repo.DirectConnection);
        Assert.Same(conn, repo.DirectReadConnection);   // modo padrão: leitura = escrita
    }

    [Fact]
    public void With_active_transaction_returns_transaction_connection()
    {
        var tb = new PostgresTransactionBase();
        var repo = new PersonQueries(new PostgresConnectionFactory("Host=primary"), tb, EmptyConfig());
        var txConnection = new NpgsqlConnection();
        tb.AddTransaction(new FakeTransaction(), txConnection);

        Assert.Same(txConnection, repo.DirectConnection);
        Assert.Same(txConnection, repo.DirectReadConnection);

        tb.Clear();
        Assert.NotSame(txConnection, repo.DirectConnection);
    }

    [Fact]
    public void Segregated_mode_routes_direct_connections()
    {
        var repo = new PersonQueriesSegregated(
            new PostgresReadConnectionFactory("Host=replica"),
            new PostgresWriteConnectionFactory("Host=primary"),
            new PostgresTransactionBase(), EmptyConfig());

        Assert.Equal("replica", new NpgsqlConnectionStringBuilder(repo.DirectReadConnection.ConnectionString).Host);
        Assert.Equal("primary", new NpgsqlConnectionStringBuilder(repo.DirectConnection.ConnectionString).Host);
    }

    private sealed class FakeTransaction : IDbTransaction
    {
        public IDbConnection? Connection => null;
        public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        public void Commit() { }
        public void Rollback() { }
        public void Dispose() { }
    }
}

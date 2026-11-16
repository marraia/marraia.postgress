using System.Data;
using Dapper;
using Marraia.Postgres.Configurations;
using Marraia.Postgres.Repositories.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Marraia.Postgres.Tests;

/// <summary>
/// Sobe um PostgreSQL real via Testcontainers. Sem Docker disponível, os testes são ignorados (Skip).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public string? ConnectionString { get; private set; }
    public string? UnavailableReason { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();

            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.ExecuteAsync("""
                CREATE SCHEMA app;
                CREATE TABLE app.person (id serial PRIMARY KEY, name text NOT NULL, surname text NOT NULL);
                CREATE TABLE app.product (id uuid PRIMARY KEY, title text NOT NULL);
                """);
        }
        catch (Exception ex)
        {
            UnavailableReason = $"Docker/PostgreSQL indisponível: {ex.GetType().Name}";
        }
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
            await _container.DisposeAsync();
    }
}

public class IntegrationTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public IntegrationTests(PostgresFixture fixture) => _fixture = fixture;

    private ServiceProvider BuildProvider(int? maxPoolSize = null, bool readReplica = false)
    {
        Skip.If(_fixture.ConnectionString is null, _fixture.UnavailableReason);

        var cs = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { ApplicationName = "escrita" };
        if (maxPoolSize.HasValue) cs.MaxPoolSize = maxPoolSize.Value;

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SchemaBD"] = "app" })
            .Build());

        if (readReplica)
        {
            // Mesmo banco com application_name diferente, para identificar qual conexão foi usada.
            var read = new NpgsqlConnectionStringBuilder(cs.ConnectionString) { ApplicationName = "leitura" };
            services.AddPostgres(connectionStringRead: read.ConnectionString, connectionStringWrite: cs.ConnectionString);
        }
        else
        {
            services.AddPostgres(cs.ConnectionString);
        }

        services.AddScoped<PersonRepository>();
        services.AddScoped<ProductRepository>();
        services.AddScoped<PersonQueries>();
        if (readReplica)
        {
            services.AddScoped<PersonQueriesSegregated>();
            services.AddScoped<PersonRepositorySegregated>();
        }
        return services.BuildServiceProvider();
    }

    [SkippableFact]
    public async Task Crud_with_serial_key()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<PersonRepository>();

        var id = await repo.InsertAsync(new Person("Fernando", "Mendes"));
        Assert.True(id > 0);

        var read = await repo.GetByIdAsync(id);
        Assert.Equal("Fernando", read!.Name);

        read.Name = "Fer";
        await repo.UpdateAsync(read);
        Assert.Equal("Fer", (await repo.GetByIdAsync(id))!.Name);

        await repo.DeleteAsync(id);
        Assert.Null(await repo.GetByIdAsync(id));
    }

    [SkippableFact]
    public async Task Insert_with_guid_key_returns_the_generated_id()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<ProductRepository>();

        var product = new Product("Livro");
        var id = await repo.InsertAsync(product);

        Assert.Equal(product.Id, id);
        Assert.NotEqual(Guid.Empty, id);
    }

    [SkippableFact]
    public async Task Repository_keeps_working_after_unit_of_work_is_disposed()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<PersonRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IPostgresUnitOfWork>();

        int id;
        using (var tx = uow.BeginTransaction())
        {
            id = await repo.InsertAsync(new Person("A", "B"));
            tx.Commit();
        }

        // Antes da v3 isto falhava: o Dispose da UnitOfWork fechava a conexão compartilhada.
        Assert.NotNull(await repo.GetByIdAsync(id));
    }

    [SkippableFact]
    public async Task Dispose_without_commit_rolls_back()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<PersonRepository>();
        var uow = (Uow.UnitOfWork)scope.ServiceProvider.GetRequiredService<IPostgresUnitOfWork>();

        int id;
        await using (await uow.BeginTransactionAsync())
        {
            id = await repo.InsertAsync(new Person("Rollback", "Auto"));
            Assert.NotNull(await repo.GetByIdAsync(id)); // visível dentro da transação
        }

        Assert.False(uow.HasActiveTransaction);
        Assert.Null(await repo.GetByIdAsync(id));
    }

    [SkippableFact]
    public async Task Parallel_operations_inside_transaction_are_serialized_on_the_same_connection()
    {
        // Pool com 1 conexão: só funciona se todas as operações da transação usarem a conexão dela.
        await using var provider = BuildProvider(maxPoolSize: 1);
        await using var scope = provider.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<PersonRepository>();
        var queries = scope.ServiceProvider.GetRequiredService<PersonQueries>();
        var uow = (Uow.UnitOfWork)scope.ServiceProvider.GetRequiredService<IPostgresUnitOfWork>();

        await uow.BeginTransactionAsync();
        var before = await queries.CountAsync();

        var ids = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(i => repo.InsertAsync(new Person($"P{i}", "Paralelo"))));

        Assert.Equal(20, ids.Distinct().Count());
        Assert.Equal(before + 20, await queries.CountWithoutTxParameterAsync());

        await uow.RollbackAsync();
        Assert.Equal(before, await queries.CountAsync());
    }

    [SkippableFact]
    public async Task Read_and_write_connections_are_routed_and_transaction_uses_write()
    {
        await using var provider = BuildProvider(readReplica: true);
        await using var scope = provider.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredService<PersonQueriesSegregated>();
        var repo = scope.ServiceProvider.GetRequiredService<PersonRepositorySegregated>();
        var uow = (Uow.UnitOfWork)scope.ServiceProvider.GetRequiredService<IPostgresUnitOfWork>();

        Assert.Equal("leitura", await queries.ReadAppNameAsync());
        Assert.Equal("escrita", await queries.WriteAppNameAsync());

        await using (await uow.BeginTransactionAsync())
        {
            // Dentro da transação, leituras vão para o primário e enxergam o que foi escrito.
            Assert.Equal("escrita", await queries.ReadAppNameAsync());
            var id = await repo.InsertAsync(new Person("Tx", "Leitura"));
            Assert.NotNull(await repo.GetByIdAsync(id));
        }
    }

    [SkippableFact]
    public async Task Direct_connection_works_without_ExecuteSafe_and_releases_connection()
    {
        // Pool de 1 conexão: só passa se o Dapper devolver a conexão ao pool após cada comando.
        await using var provider = BuildProvider(maxPoolSize: 1);
        await using var scope = provider.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredService<PersonQueries>();
        var repo = scope.ServiceProvider.GetRequiredService<PersonRepository>();

        var before = await queries.CountDirectAsync();
        var id = await queries.InsertDirectAsync(new Person("Direto", "SemWrapper"));
        Assert.NotNull(await repo.GetByIdAsync(id)); // outra conexão do pool: a anterior foi liberada
        Assert.Equal(before + 1, await queries.CountDirectAsync());
        Assert.Equal(ConnectionState.Closed, queries.DirectConnection.State);
    }

    [SkippableFact]
    public async Task Direct_connection_participates_in_unit_of_work()
    {
        await using var provider = BuildProvider(maxPoolSize: 1);
        await using var scope = provider.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredService<PersonQueries>();
        var uow = (Uow.UnitOfWork)scope.ServiceProvider.GetRequiredService<IPostgresUnitOfWork>();

        var before = await queries.CountDirectAsync();

        await using (await uow.BeginTransactionAsync())
        {
            await queries.InsertDirectAsync(new Person("Tx", "Direto"));
            Assert.Equal(before + 1, await queries.CountDirectAsync());
        } // rollback automático

        Assert.Equal(before, await queries.CountDirectAsync());
    }

    [SkippableFact]
    public async Task Direct_connections_are_routed_in_segregated_mode()
    {
        await using var provider = BuildProvider(readReplica: true);
        await using var scope = provider.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredService<PersonQueriesSegregated>();

        Assert.Equal("leitura", await queries.DirectReadAppNameAsync());
        Assert.Equal("escrita", await queries.DirectWriteAppNameAsync());
    }

    [SkippableFact]
    public async Task Default_mode_repository_works_unchanged_when_read_write_is_configured()
    {
        await using var provider = BuildProvider(readReplica: true);
        await using var scope = provider.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredService<PersonQueries>();
        var repo = scope.ServiceProvider.GetRequiredService<PersonRepository>();

        // Sem optar pela segregação, tudo vai para a conexão padrão (escrita).
        Assert.Equal("escrita", await queries.AppNameAsync());
        var id = await repo.InsertAsync(new Person("Padrao", "Modo"));
        Assert.NotNull(await repo.GetByIdAsync(id));
    }

    [SkippableFact]
    public async Task Parallel_operations_without_transaction_use_independent_connections()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<PersonRepository>();

        var ids = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(i => repo.InsertAsync(new Person($"S{i}", "SemTx"))));

        var all = await Task.WhenAll(ids.Select(repo.GetByIdAsync));
        Assert.All(all, Assert.NotNull);
    }
}

using System.Data;
using System.ComponentModel;
using Dapper;
using Marraia.Postgres.Attributes;
using Marraia.Postgres.Core;
using Marraia.Postgres.Repositories;
using Marraia.Postgres.Repositories.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Marraia.Postgres.Tests;

// Sem [Description]: o nome da tabela é o nome da classe (antes lançava NullReferenceException).
public class Person : Entity<int>
{
    public Person() { }
    public Person(string name, string surname) { Name = name; Surname = surname; }

    public string Name { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;

    [IgnoreProperty]
    public string FullName => $"{Name} {Surname}";

    public void SetId(int id) => Id = id;
}

[Description("product")]
public class Product : Entity<Guid>
{
    public Product() { }
    public Product(string title) { Id = Guid.NewGuid(); Title = title; }

    public string Title { get; set; } = string.Empty;

    [Description("não persistida")]
    public string Transient { get; set; } = string.Empty;
}

public class PersonRepository : RepositoryBase<Person, int>
{
    public PersonRepository(IPostgresConnectionFactory factory, IPostgresTransactionBase tx, IConfiguration configuration)
        : base(factory, tx, configuration) { }
}

public class ProductRepository : RepositoryBase<Product, Guid>
{
    public ProductRepository(IPostgresConnectionFactory factory, IPostgresTransactionBase tx, IConfiguration configuration)
        : base(factory, tx, configuration) { }
}

public class PersonQueries : RepositoryStandard<Person, int>
{
    public PersonQueries(IPostgresConnectionFactory factory, IPostgresTransactionBase tx, IConfiguration configuration)
        : base(factory, tx, configuration) { }

    public Task<int> CountAsync()
        => ExecuteSafeAsync((conn, tx) => conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {FullTableName}", transaction: tx));

    public Task<int> CountWithoutTxParameterAsync()
        => ExecuteSafeAsync(conn => conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {FullTableName}"));

    public Task<string> AppNameAsync()
        => ExecuteSafeAsync((conn, tx) => conn.ExecuteScalarAsync<string>(Sql.AppName, transaction: tx))!;

    // Uso direto, sem ExecuteSafeAsync.
    public Task<int> CountDirectAsync()
        => _connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {FullTableName}", transaction: _transaction);

    public Task<int> InsertDirectAsync(Person person)
        => _connection.ExecuteScalarAsync<int>(
            $"INSERT INTO {FullTableName} (name, surname) VALUES (@Name, @Surname) RETURNING id", person, _transaction);

    public IDbConnection DirectConnection => _connection;
    public IDbConnection DirectReadConnection => _readConnection;
}

// application_name identifica qual connection string (leitura ou escrita) atendeu a operação.
internal static class Sql
{
    public const string AppName = "SELECT current_setting('application_name')";
}

// Modo segregado: construtor com as factories de leitura e escrita.
public class PersonQueriesSegregated : RepositoryStandard<Person, int>
{
    public PersonQueriesSegregated(IPostgresReadConnectionFactory read, IPostgresWriteConnectionFactory write,
                                   IPostgresTransactionBase tx, IConfiguration configuration)
        : base(read, write, tx, configuration) { }

    public Task<string> ReadAppNameAsync()
        => ExecuteReadAsync((conn, tx) => conn.ExecuteScalarAsync<string>(Sql.AppName, transaction: tx))!;

    public Task<string> WriteAppNameAsync()
        => ExecuteWriteAsync((conn, tx) => conn.ExecuteScalarAsync<string>(Sql.AppName, transaction: tx))!;

    // Uso direto no modo segregado.
    public Task<string> DirectReadAppNameAsync()
        => _readConnection.ExecuteScalarAsync<string>(Sql.AppName, transaction: _transaction)!;

    public Task<string> DirectWriteAppNameAsync()
        => _connection.ExecuteScalarAsync<string>(Sql.AppName, transaction: _transaction)!;

    public IDbConnection DirectConnection => _connection;
    public IDbConnection DirectReadConnection => _readConnection;
}

public class PersonRepositorySegregated : RepositoryBase<Person, int>
{
    public PersonRepositorySegregated(IPostgresReadConnectionFactory read, IPostgresWriteConnectionFactory write,
                                      IPostgresTransactionBase tx, IConfiguration configuration)
        : base(read, write, tx, configuration) { }
}

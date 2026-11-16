# Marraia.Postgres

Biblioteca para acessar o PostgreSQL em aplicações .NET (8, 9 e 10) com **Dapper**, **Repository** e **Unit of Work**.

- Conexão **por operação**: cada comando pega uma conexão do pool (`NpgsqlDataSource`) e a devolve logo em seguida.
- **Leitura e escrita segregadas** (opcional, por repositório): consultas na réplica, comandos e transações no primário.
- **Unit of Work** com conexão dedicada, rollback automático e liberação imediata da conexão no Commit/Rollback.
- **Operações paralelas seguras**: dentro de uma transação, chamadas simultâneas (`Task.WhenAll`) são serializadas por um semáforo, porque uma conexão PostgreSQL só executa um comando por vez.
- CRUD gerado automaticamente (com cache), usando `RETURNING` para devolver a chave (serial, identity ou `uuid`).

## Instalação

```
dotnet add package Marraia.Postgres
```

## Configuração

**appsettings.json**

```json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=meubanco;Username=postgres;Password=senha;Maximum Pool Size=50"
  },
  "SchemaBD": "public"
}
```

`SchemaBD` é opcional. Sem ele, as tabelas são usadas sem prefixo de schema (vale o `search_path` do banco).

**Program.cs**

```csharp
using Marraia.Postgres.Configurations;

builder.Services.AddPostgres(builder.Configuration.GetConnectionString("Postgres")!);
```

### Leitura e escrita separadas (réplica)

```json
"ConnectionStrings": {
  "PostgresRead":  "Host=replica;Database=meubanco;Username=leitor;Password=senha",
  "PostgresWrite": "Host=primary;Database=meubanco;Username=app;Password=senha"
}
```

```csharp
builder.Services.AddPostgres(
    connectionStringRead:  builder.Configuration.GetConnectionString("PostgresRead")!,
    connectionStringWrite: builder.Configuration.GetConnectionString("PostgresWrite")!);
```

A segregação é **opcional por repositório**: só os repositórios que recebem as factories de leitura e escrita no construtor (veja [Modo segregado](#modo-segregado-leitura-e-escrita)) separam as conexões. Os demais continuam no modo padrão, usando o banco de escrita.

`AddPostgres` registra:

| Serviço | Lifetime | Papel |
|---|---|---|
| `IPostgresConnectionFactory` | Singleton | Conexão padrão (no modo segregado, é o banco de escrita). |
| `IPostgresReadConnectionFactory` | Singleton | Banco de leitura (só com duas connection strings). |
| `IPostgresWriteConnectionFactory` | Singleton | Banco de escrita (só com duas connection strings). |
| `IPostgresTransactionBase` | Scoped | Guarda a transação ativa do request. |
| `IPostgresUnitOfWork` | Scoped | Inicia, confirma e desfaz transações (sempre no banco de escrita). |

Os tipos com prefixo `IPostgres` permitem usar esta biblioteca junto com outras bibliotecas Marraia (por exemplo, Marraia.MySql) no mesmo projeto. Chamar `AddPostgres` duas vezes lança `InvalidOperationException`; para segregar leitura e escrita use a sobrecarga com duas connection strings numa única chamada.

## Entidade

```csharp
using System.ComponentModel;
using Marraia.Postgres.Attributes;
using Marraia.Postgres.Core;

[Description("person")]          // nome da tabela (opcional; padrão = nome da classe)
public class Person : Entity<int> // chave: int/long (gerada pelo banco) ou Guid (gerada pela aplicação)
{
    public string Name { get; set; } = "";
    public string Surname { get; set; } = "";

    [IgnoreProperty]             // não é persistida
    public string FullName => $"{Name} {Surname}";
}
```

Regras de mapeamento:
- A chave se chama `Id`. Ela entra no `INSERT` apenas quando é `Guid`; nos demais tipos é gerada pelo banco.
- Propriedades com `[IgnoreProperty]` ou `[Description]` não entram no `INSERT`/`UPDATE`.
- Os nomes não levam aspas, então o PostgreSQL os converte para minúsculas (`Person` vira `person`).

## Repositório com CRUD pronto

```csharp
public interface IPersonRepository : IRepositoryBase<Person, int> { }

public class PersonRepository : RepositoryBase<Person, int>, IPersonRepository
{
    public PersonRepository(IPostgresConnectionFactory connectionFactory,
                            IPostgresTransactionBase transactionBase,
                            IConfiguration configuration)
        : base(connectionFactory, transactionBase, configuration) { }

    // Métodos customizados: use _connection diretamente com o Dapper.
    public Task<IEnumerable<Person>> GetBySurnameAsync(string surname)
        => _connection.QueryAsync<Person>(
               $"SELECT * FROM {FullTableName} WHERE surname = @surname", new { surname }, _transaction);
}
```

Métodos herdados: `InsertAsync`, `UpdateAsync`, `DeleteAsync`, `GetByIdAsync` e `GetAllAsync`.

## Repositório para consultas próprias

```csharp
public class PersonQueries : RepositoryStandard<Person, int>
{
    public PersonQueries(IPostgresConnectionFactory connectionFactory,
                         IPostgresTransactionBase transactionBase,
                         IConfiguration configuration,
                         ILogger<PersonQueries> logger)
        : base(connectionFactory, transactionBase, configuration, logger) { }

    public Task<int> CountAsync()
        => _connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {FullTableName}", transaction: _transaction);

    public Task ArchiveAsync(int id)
        => _connection.ExecuteAsync(
               $"UPDATE {FullTableName} SET archived = true WHERE id = @id", new { id }, _transaction);
}
```

## Usando a conexão: direto ou com `ExecuteSafeAsync`

Os repositórios (`RepositoryBase` e `RepositoryStandard`) oferecem duas formas de falar com o banco.

### 1. Direto, com `_connection` (o caso comum)

```csharp
var pessoas = await _connection.QueryAsync<Person>(sql, param, _transaction);
```

| Membro | O que é |
|---|---|
| `_connection` | Com transação ativa, a conexão da transação. Sem transação, uma conexão **fechada** do repositório. No modo segregado, é a conexão de escrita. |
| `_readConnection` | Igual a `_connection`, mas no modo segregado aponta para a leitura (réplica). |
| `_transaction` | A transação ativa da UnitOfWork, ou `null`. |

Como funciona sem transação: quando o Dapper recebe uma conexão **fechada**, ele a abre, executa o comando e a fecha em seguida, devolvendo-a ao pool. Por isso a conexão nunca fica presa entre uma operação e outra, e cada comando é atendido por uma conexão física do pool.

Para que isso funcione:
- **Não** chame `Open()`, `Close()` nem `Dispose()` em `_connection`. Se você abrir a conexão manualmente, ela fica presa até o fim do request.
- Repasse `_transaction` ao Dapper. No Npgsql, comandos na conexão da transação rodam na transação de qualquer forma, mas passar o parâmetro deixa o código explícito e portável.

### 2. Com `ExecuteSafeAsync` (casos específicos)

```csharp
await ExecuteSafeAsync(async (conn, tx) => { /* conn já aberta */ });
```

`ExecuteSafeAsync` abre uma conexão só para o bloco (ou usa a da transação, com acesso exclusivo) e a mantém aberta até o bloco terminar. **Use quando:**

| Situação | Por que o uso direto não serve |
|---|---|
| **Chamadas em paralelo** no mesmo repositório (`Task.WhenAll`) | `_connection` é um único objeto. Duas chamadas simultâneas tentam abrir a mesma conexão e o Npgsql lança erro. `ExecuteSafeAsync` dá uma conexão própria para cada chamada. Dentro de uma transação, ele serializa as chamadas pelo semáforo. |
| **Vários comandos que precisam da mesma sessão**: tabela temporária, `SET`/`SET LOCAL`, advisory lock, `LISTEN`, cursor | No uso direto, cada comando pode cair numa conexão física diferente do pool. Com `ExecuteSafeAsync`, todos os comandos do bloco usam a mesma conexão. |
| **Leitura não bufferizada** (`QueryUnbufferedAsync`, `buffered: false`) ou **`QueryMultipleAsync`** consumido aos poucos | A conexão precisa continuar aberta enquanto você lê os resultados. |
| **Log de erro automático** (`RepositoryStandard` com `ILogger`) | `ExecuteSafeAsync` registra a exceção com o nome da entidade antes de relançá-la. |

Em todos os outros casos, como consultas e comandos simples feitos em sequência (com ou sem UnitOfWork), use `_connection` diretamente.

> Os métodos prontos do `RepositoryBase` (`InsertAsync`, `GetByIdAsync` etc.) já usam o mecanismo do `ExecuteSafeAsync` internamente, então podem ser chamados em paralelo com segurança.

`ExecuteSafeAsync` tem sobrecargas com `(conn, tx)` e só com `(conn)`. As duas participam da transação ativa. Não chame `Open`, `Close` ou `Dispose` na conexão recebida.

## Modo segregado (leitura e escrita)

Requer `AddPostgres(connectionStringRead, connectionStringWrite)`. Basta trocar o construtor do repositório para receber as factories de leitura e escrita:

```csharp
public class PersonRepository : RepositoryBase<Person, int>, IPersonRepository
{
    public PersonRepository(IPostgresReadConnectionFactory readConnectionFactory,
                            IPostgresWriteConnectionFactory writeConnectionFactory,
                            IPostgresTransactionBase transactionBase,
                            IConfiguration configuration)
        : base(readConnectionFactory, writeConnectionFactory, transactionBase, configuration) { }
}

public class PersonQueries : RepositoryStandard<Person, int>
{
    public PersonQueries(IPostgresReadConnectionFactory readConnectionFactory,
                         IPostgresWriteConnectionFactory writeConnectionFactory,
                         IPostgresTransactionBase transactionBase,
                         IConfiguration configuration)
        : base(readConnectionFactory, writeConnectionFactory, transactionBase, configuration) { }

    // Uso direto: _readConnection (réplica) e _connection (primário).
    public Task<IEnumerable<Person>> GetAllActiveAsync()
        => _readConnection.QueryAsync<Person>($"SELECT * FROM {FullTableName} WHERE active", transaction: _transaction);

    public Task ArchiveAsync(int id)
        => _connection.ExecuteAsync($"UPDATE {FullTableName} SET archived = true WHERE id = @id", new { id }, _transaction);

    // Nos casos da tabela "Com ExecuteSafeAsync": ExecuteReadAsync / ExecuteWriteAsync.
    public Task<Person[]> GetManyInParallelAsync(int[] ids)
        => Task.WhenAll(ids.Select(id => ExecuteReadAsync((conn, tx) =>
               conn.QuerySingleAsync<Person>($"SELECT * FROM {FullTableName} WHERE id = @id", new { id }, tx))));
}
```

| Operação | Sem transação | Com transação (UnitOfWork) |
|---|---|---|
| `GetByIdAsync`, `GetAllAsync`, `_readConnection`, `ExecuteReadAsync` | leitura (réplica) | conexão da transação (escrita) |
| `InsertAsync`, `UpdateAsync`, `DeleteAsync`, `_connection`, `ExecuteWriteAsync`, `ExecuteSafeAsync` | escrita (primário) | conexão da transação (escrita) |

> ⚠️ Réplicas têm atraso de replicação: uma leitura logo após uma escrita, **fora de transação**, pode não enxergar o dado ainda. Quando precisar ler o que acabou de gravar, faça a leitura dentro da transação ou use `ExecuteWriteAsync`.

`_readConnection`, `ExecuteReadAsync` e `ExecuteWriteAsync` também funcionam num repositório em modo padrão, usando a conexão única. Assim o código do repositório não precisa mudar se você trocar de modo depois.

## Transação (Unit of Work)

```csharp
public class PersonAppService(IPersonRepository repository, IPostgresUnitOfWork unitOfWork)
{
    public async Task InsertAsync(CancellationToken ct)
    {
        await using var uow = await unitOfWork.BeginTransactionAsync(cancellationToken: ct);

        await repository.InsertAsync(new Person { Name = "Fernando", Surname = "Mendes" });
        await repository.InsertAsync(new Person { Name = "Maria", Surname = "Silva" });

        await uow.CommitAsync(ct);
    } // sem CommitAsync (ex.: exceção), o DisposeAsync faz rollback automaticamente
}
```

A API síncrona também está disponível (`BeginTransaction`, `Commit`, `Rollback` e `Dispose`). Detalhes:
- Durante a transação, todos os repositórios do mesmo escopo usam a mesma conexão.
- Commit e Rollback devolvem a conexão ao pool imediatamente.
- `Rollback()` sem transação ativa não faz nada, para não mascarar a exceção original de um `Commit()` que falhou.
- Não é possível iniciar duas transações simultâneas na mesma `UnitOfWork`.

### Operações paralelas

```csharp
await using var uow = await unitOfWork.BeginTransactionAsync();
await Task.WhenAll(people.Select(repository.InsertAsync)); // serializadas na conexão da transação
await uow.CommitAsync();
```

- **Dentro de uma transação:** as operações são enfileiradas por um `SemaphoreSlim` por request. Ele é reentrante, então chamadas aninhadas não travam.
- **Fora de transação:** cada operação recebe sua própria conexão, e o limite de concorrência é o `Maximum Pool Size` da connection string.

## Migração da 2.x para a 3.0

| 2.x | 3.0 |
|---|---|
| `IPostgresDbConnection` no construtor | `IPostgresConnectionFactory` |
| `_connection.QueryAsync(..., transaction: _transactionBase.GetDbTransaction())` | Continua igual (ou use `_transaction`). Só o construtor muda. Não abra nem feche `_connection`. |
| `ITransactionBase.AddTransaction(tx)` | `AddTransaction(tx, connection)` (só afeta implementações próprias) |
| `AddDapperPostgres` | `AddPostgres` (`AddDapperPostgres` foi removido) |
| — | `AddPostgres(connectionStringRead, connectionStringWrite)` + construtor com `IPostgresReadConnectionFactory`/`IPostgresWriteConnectionFactory` |
| `InsertAsync` com `currval(...)` | `INSERT ... RETURNING Id` |
| Repositórios implementavam `IDisposable` e fechavam a conexão | Os repositórios não são donos de conexão e não implementam mais `IDisposable` |

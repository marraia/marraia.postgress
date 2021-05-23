# Utilização do SQL Server em aplicações em .Net Core

Adapter para conexão com o SQL Server  

## Intalação via Nuget
PM > Install-Package Marraia.SqlServer


## Arquivo de configuração - String de conexão

Primeiro passo é adicionar a string de conexão de seu banco de dados SQL Server em seu arquivo de configuração **appsettings.json**

```
"Connection": "Data Source=localhost;Initial Catalog=TesteDb;User Id=sa;Password=123Aa321;"
```

## ORM - Dapper
Nesta biblioteca comtempla o uso do ORM Dapper. Você pode utilizar para se conectar em seu banco de dados.

## Dapper - Injetar o uso do Dapper em sua aplicação

No arquivo Startup.cs de sua aplicação adicione no método **ConfigureServices** o middleware em específico:
```
        public void ConfigureServices(IServiceCollection services)
        {
            ..
            ..
            services.AddDapperSqlServer(Configuration.GetSection("Connection").Value);
        }
```
  
## Herança de Interface de Repositório Base
Na interface de repositório de sua classe de domínio, precisaremos da herança da interface **IRepositoryBase<ClassDomain, PrimaryKey>**
Onde **ClassDomain** informe sua classe de domínio
Onde **PrimaryKey** informe o tipo da identificação do objeto no SQL Server

````
    public interface IPersonRepository : IRepositoryBase<Person, int>
    {
    }
````

## Herança de Repositório Base
Em seu repositório use a herança da classe **RepositoryBase<ClassDomain, PrimaryKey>**.
Onde **ClassDomain** informe sua classe de domínio
Onde **PrimaryKey** informe o tipo da identificação do objeto no SQL Server

````
public class PersonRepository : RepositoryBase<Person, int>, IPersonRepository
{
   public PersonRepository(IDbConnection connection,
                            ITransactionBase transactionBase) 
     : base (connection, transactionBase)
   {
           
   }
}
````
A interface **ITransactionBase**, é responsavel pelo controle de transação, caso você utilize transação em seus comandos. Veremos como fazer isso no tópico **Transação**.

Com essa herança, você terá os métodos:
- InsertAsync(ClassDomain)
- UpdateAsync(ClassDomain)
- DeleteAsync(PrimaryKey)
- GetByIdAsync(PrimaryKey)
- GetAllAsync()

## Sobrescrita dos métodos base

Caso necessite você poderá fazer a sobrescrita dos métodos da classe base **RepositoryBase<ClassDomain, PrimaryKey>**
```
    public class PersonRepository : RepositoryBase<Person, int>, IPersonRepository
    {
        public PersonRepository(IDbConnection connection,
                                ITransactionBase transactionBase)
            : base(connection, transactionBase)
        {

        }

        public override Task InsertAsync(Person entity)
        {
            return _connection
                    .ExecuteAsync("INSERT INTO PERSON VALUES (@name, @surname)", entity);
        }

        public override Task<Person> GetByIdAsync(int id)
        {
            return base.GetByIdAsync(id);
        }
    }
```
Perceba, que para obter a conexão com o SQL SERVER, existe a propriedade **_connection**, que já está injetada com a sua classe de domínio.

## Transação com Dapper
Caso você precise controlar transação em seu projeto, segue um exemplo da utilização com o design de Unit of Work.
Faça a inversão de controle da interface **IUnitOfWork**, que já está injetada em seu projeto conforme tópico **Dapper - Injetar o uso do Dapper em sua aplicação**

````
public class PersonAppService : IPersonAppService
{
    readonly IPersonRepository _personRepository;
    readonly IUnitOfWork _unitOfWork;

    public PersonAppService(IPersonRepository personRepository,
                            IUnitOfWork unitOfWork)
    {
        _personRepository = personRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task InsertAsync()
    {
        using (transaction = _unitofWork.BeginTransaction())
        {
            try
            {
                await _personRepository
                        .InsertAsync(new Person("Fernando", "Abreu Mendes"))
                        .ConfigureAwait(false);

                transaction.Commit();
            }
            catch(Exception ex)
            {
                transaction.Rollback();
            }
        }
    }
}
````

using Marraia.Postgres.Repositories.Interfaces;
using Marraia.Postgres.Uow;
using Marraia.Postgres.Uow.Interfaces;

namespace Marraia.Postgres.Configurations
{
    public class PostgresUnitOfWork : UnitOfWork, IPostgresUnitOfWork
    {
        public PostgresUnitOfWork(IPostgresDbConnection connection, 
                                  ITransactionBase transactionBase) 
        : base(connection, transactionBase) {}
    }
}

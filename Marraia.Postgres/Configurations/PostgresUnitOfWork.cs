using Marraia.Postgres.Repositories.Interfaces;
using Marraia.Postgres.Uow;

namespace Marraia.Postgres.Configurations
{
    public class PostgresUnitOfWork : UnitOfWork, IPostgresUnitOfWork
    {
        public PostgresUnitOfWork(IPostgresDbConnection connection, 
                                  IPostgresTransactionBase transactionBase) 
        : base(connection, transactionBase) {}
    }
}

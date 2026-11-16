using Marraia.Postgres.Repositories.Interfaces;
using Marraia.Postgres.Uow;

namespace Marraia.Postgres.Configurations
{
    public class PostgresUnitOfWork : UnitOfWork, IPostgresUnitOfWork
    {
        public PostgresUnitOfWork(IPostgresConnectionFactory connectionFactory,
                                  IPostgresTransactionBase transactionBase)
            : base(connectionFactory, transactionBase) { }
    }
}

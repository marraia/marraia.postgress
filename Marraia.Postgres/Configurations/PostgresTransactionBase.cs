using Marraia.Postgres.Repositories.Interfaces;
using Marraia.Postgres.Uow.Transactions;

namespace Marraia.Postgres.Configurations
{
    public class PostgresTransactionBase : TransactionBase, IPostgresTransactionBase { }
}

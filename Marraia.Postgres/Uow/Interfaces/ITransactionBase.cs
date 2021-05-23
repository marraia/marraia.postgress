using System.Data;

namespace Marraia.Postgres.Uow.Interfaces
{
    public interface ITransactionBase
    {
        void AddTransaction(IDbTransaction dbTransaction);
        IDbTransaction GetDbTransaction();
    }
}

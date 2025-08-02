using Marraia.Postgres.Repositories.Interfaces;
using Marraia.Postgres.Uow.Interfaces;
using System;
using System.Data;

namespace Marraia.Postgres.Uow
{
    public class UnitOfWork : IPostgresUnitOfWork, IDisposable
    {
        private readonly IPostgresDbConnection _connection;
        readonly ITransactionBase _transactionBase;
        private IDbTransaction DbTransaction { get; set; }

        public UnitOfWork(IPostgresDbConnection connection,
                          ITransactionBase transactionBase)
        {
            _connection = connection;
            _transactionBase = transactionBase;
        }

        public UnitOfWork BeginTransaction()
        {
            DbTransaction = _connection
                                .BeginTransaction();

            _transactionBase
                .AddTransaction(DbTransaction);

            return this;
        }

        public bool Commit()
        {
            if (DbTransaction == null)
                return false;

            DbTransaction
                .Commit();

            return true;
        }

        public void Rollback()
        {
            DbTransaction
                .Rollback();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            _connection.Close();
            DbTransaction = null;
        }
    }
}

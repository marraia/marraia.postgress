using Marraia.Postgres.Comum;
using Marraia.Postgres.Core;
using Marraia.Postgres.Uow.Interfaces;
using Microsoft.Extensions.Configuration;
using System;
using System.Data;

namespace Marraia.Postgres.Repositories
{
    public class RepositoryStandard<TEntity, TKey> : CommonConfiguration<TEntity>, IDisposable
            where TEntity : Entity<TKey>
            where TKey : struct
    {
        protected readonly IDbConnection _connection;
        protected readonly ITransactionBase _transactionBase;

        protected RepositoryStandard(IDbConnection connection,
                                       ITransactionBase transactionBase,
                                       IConfiguration configuration)
        : base (configuration)
        {
            _connection = connection;

            if (_connection.State == ConnectionState.Closed)
            {
                _connection.Open();
            }

            _transactionBase = transactionBase;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            _connection
                .Close();

            _connection
                .Dispose();
        }
    }
}

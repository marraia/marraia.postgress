using Marraia.Postgres.Comum;
using Marraia.Postgres.Core;
using Marraia.Postgres.Repositories.Interfaces;
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
        protected readonly IPostgresDbConnection _connection;
        protected readonly IPostgresTransactionBase _transactionBase;

        protected RepositoryStandard(IPostgresDbConnection connection,
                                       IPostgresTransactionBase transactionBase,
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

using Dapper;
using Marraia.Postgres.Comum;
using Marraia.Postgres.Repositories.Interfaces;
using Marraia.Postgres.Uow.Interfaces;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace Marraia.Postgres.Repositories
{
    public abstract class RepositoryBase<TEntity, TKey> : CommonConfiguration<TEntity>, IRepositoryBase<TEntity, TKey>, IDisposable
                where TEntity : class
                where TKey : struct
    {
        protected readonly IDbConnection _connection;
        private readonly ITransactionBase _transactionBase;

        protected RepositoryBase(IDbConnection connection,
                                   ITransactionBase transactionBase,
                                   IConfiguration configuration)
            : base(configuration)
        {
            _connection = connection;
            _connection.Open();

            _transactionBase = transactionBase;
        }

        public virtual async Task<TKey> InsertAsync(TEntity entity)
        {
            var query = GenerateInsertQuery();

            var id = await _connection
                            .ExecuteScalarAsync<TKey>(query,
                                            entity,
                                            transaction: _transactionBase.GetDbTransaction())
                            .ConfigureAwait(false);
            return id;
        }

        public virtual async Task UpdateAsync(TEntity entity)
        {
            var query = GenerateUpdateQuery();

            await _connection
                    .ExecuteAsync(query,
                                  entity,
                                  transaction: _transactionBase.GetDbTransaction())
                    .ConfigureAwait(false);
        }
        public virtual async Task DeleteAsync(TKey id)
        {
            await _connection
                    .ExecuteAsync($"DELETE FROM {typeof(TEntity).Name} WHERE Id=@Id",
                                    new { Id = id },
                                    transaction: _transactionBase.GetDbTransaction())
                    .ConfigureAwait(false);
        }
        public virtual async Task<TEntity> GetByIdAsync(TKey id)
        {
            return await _connection
                            .QuerySingleOrDefaultAsync<TEntity>($"SELECT * FROM {typeof(TEntity).Name} WHERE Id=@Id", new { Id = id })
                            .ConfigureAwait(false);
        }
        public virtual async Task<IEnumerable<TEntity>> GetAllAsync()
        {
            return await _connection
                            .QueryAsync<TEntity>($"SELECT * FROM {typeof(TEntity).Name}")
                            .ConfigureAwait(false);
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

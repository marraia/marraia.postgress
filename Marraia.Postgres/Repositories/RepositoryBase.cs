using Dapper;
using Marraia.Postgres.Comum;
using Marraia.Postgres.Core;
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
                where TEntity : Entity<TKey>
                where TKey : struct
    {
        protected readonly IPostgresDbConnection _connection;
        private readonly IPostgresTransactionBase _transactionBase;

        protected RepositoryBase(IPostgresDbConnection connection,
                                   IPostgresTransactionBase transactionBase,
                                   IConfiguration configuration)
            : base(configuration)
        {
            _connection = connection;

            if (_connection.State == ConnectionState.Closed)
            {
                _connection.Open();
            }

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
            var sql = GenerateDeleteQuery();

            await _connection
                    .ExecuteAsync(sql,
                                    new { Id = id },
                                    transaction: _transactionBase.GetDbTransaction())
                    .ConfigureAwait(false);
        }
        public virtual async Task<TEntity> GetByIdAsync(TKey id)
        {
            var sql = GenerateSelectByIdQuery();

            return await _connection
                            .QuerySingleOrDefaultAsync<TEntity>(sql, new { Id = id })
                            .ConfigureAwait(false);
        }
        public virtual async Task<IEnumerable<TEntity>> GetAllAsync()
        {
            var sql = GenerateSelectAllQuery();

            return await _connection
                            .QueryAsync<TEntity>(sql)
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

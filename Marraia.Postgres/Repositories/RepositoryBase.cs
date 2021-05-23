using Dapper;
using Marraia.Postgres.Repositories.Interfaces;
using Marraia.Postgres.Uow.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Marraia.Postgres.Repositories
{
    public abstract class RepositoryBase<TEntity, TKey> : IRepositoryBase<TEntity, TKey>, IDisposable
                where TEntity : class
                where TKey : struct
    {
        private const int RemoveCaracteres = 1;

        protected readonly IDbConnection _connection;
        private readonly ITransactionBase _transactionBase;
        private IEnumerable<PropertyInfo> GetProperties => typeof(TEntity).GetProperties();

        protected RepositoryBase(IDbConnection connection,
                                        ITransactionBase transactionBase)
        {
            _connection = connection;
            _connection.Open();

            _transactionBase = transactionBase;
        }

        public virtual async Task InsertAsync(TEntity entity)
        {
            var query = GenerateInsertQuery();

            await _connection
                    .ExecuteAsync(query,
                                  entity,
                                  transaction: _transactionBase.GetDbTransaction())
                    .ConfigureAwait(false);
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

        protected string GenerateUpdateQuery()
        {
            var query = new StringBuilder($"UPDATE {typeof(TEntity).Name} SET ");
            var properties = GetPropertiesByEntity(GetProperties);

            properties.ForEach(property =>
            {
                if (!property.Equals("Id"))
                {
                    query.Append($"{property}=@{property},");
                }
            });

            query.Remove(query.Length - RemoveCaracteres, RemoveCaracteres);
            query.Append(" WHERE Id=@Id");

            return query.ToString();
        }

        private string GenerateInsertQuery()
        {
            var query = new StringBuilder($"INSERT INTO {typeof(TEntity).Name} ");
            query.Append("(");

            var properties = GetPropertiesByEntity(GetProperties);

            properties.ForEach(property =>
            {
                if (!property.Equals("Id"))
                    query.Append($"[{property}],");
            });

            query
                .Remove(query.Length - RemoveCaracteres, RemoveCaracteres)
                .Append(") VALUES (");

            properties.ForEach(property =>
            {
                if (!property.Equals("Id"))
                    query.Append($"@{property},");
            });

            query
                .Remove(query.Length - RemoveCaracteres, RemoveCaracteres)
                .Append(")");

            return query.ToString();
        }

        private static List<string> GetPropertiesByEntity(IEnumerable<PropertyInfo> properties)
        {
            return (from property in properties
                    let attributes = property.GetCustomAttributes(typeof(DescriptionAttribute), false)
                    where attributes.Length <= 0
                    select property.Name).ToList();
        }
    }
}

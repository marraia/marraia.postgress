using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Marraia.Postgres.Repositories.Interfaces
{
    public interface IRepositoryBase<TEntity, TKey> : IDisposable
        where TEntity : class
        where TKey : struct
    {
        Task InsertAsync(TEntity entity);
        Task UpdateAsync(TEntity entity);
        Task DeleteAsync(TKey id);
        Task<TEntity> GetByIdAsync(TKey id);
        Task<IEnumerable<TEntity>> GetAllAsync();
    }
}

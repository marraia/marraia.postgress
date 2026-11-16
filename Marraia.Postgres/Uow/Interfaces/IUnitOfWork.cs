using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace Marraia.Postgres.Uow.Interfaces
{
    public interface IUnitOfWork
    {
        /// <summary>
        /// Abre uma conexão dedicada e inicia uma transação. Os repositórios do mesmo escopo
        /// passam a executar nessa conexão até o Commit/Rollback/Dispose.
        /// </summary>
        UnitOfWork BeginTransaction(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted);

        /// <inheritdoc cref="BeginTransaction(IsolationLevel)"/>
        Task<UnitOfWork> BeginTransactionAsync(
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
            CancellationToken cancellationToken = default);
    }
}

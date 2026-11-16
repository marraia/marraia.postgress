using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace Marraia.Postgres.Uow.Interfaces
{
    /// <summary>
    /// Guarda a transação ativa do escopo (request) junto com a conexão a que ela pertence,
    /// para que os repositórios executem seus comandos nessa mesma conexão.
    /// </summary>
    public interface ITransactionBase
    {
        /// <summary>
        /// Registra a transação ativa e a conexão a que ela pertence.
        /// </summary>
        void AddTransaction(IDbTransaction dbTransaction, IDbConnection connection);

        IDbTransaction? GetDbTransaction();

        IDbConnection? GetConnection();

        /// <summary>
        /// Limpa a transação/conexão registradas ao final da unidade de trabalho (commit ou rollback).
        /// </summary>
        void Clear();

        /// <summary>
        /// Executa <paramref name="action"/> na conexão da transação ativa, com acesso exclusivo:
        /// uma conexão PostgreSQL só executa um comando por vez, então chamadas paralelas
        /// (ex.: <c>Task.WhenAll</c>) dentro da mesma transação são serializadas por um semáforo.
        /// O semáforo é reentrante no mesmo fluxo assíncrono (chamadas aninhadas não travam).
        /// </summary>
        /// <returns>
        /// <c>Executed = false</c> quando não há transação ativa; nesse caso o chamador deve usar
        /// uma conexão própria.
        /// </returns>
        Task<(bool Executed, T Result)> TryExecuteInTransactionAsync<T>(
            Func<IDbConnection, IDbTransaction, Task<T>> action,
            CancellationToken cancellationToken = default);
    }
}

using Marraia.Postgres.Uow.Interfaces;
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace Marraia.Postgres.Uow.Transactions
{
    public class TransactionBase : ITransactionBase
    {
        // Uma conexão Npgsql não aceita comandos concorrentes ("A command is already in progress").
        // Fora de transação cada operação usa sua própria conexão do pool, então o semáforo só é
        // necessário quando várias operações compartilham a conexão da transação ativa.
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        // Marca o fluxo assíncrono que já detém o semáforo, permitindo reentrância
        // (ex.: um ExecuteSafeAsync que chama outro método de repositório) sem deadlock.
        private readonly AsyncLocal<bool> _heldByCurrentFlow = new AsyncLocal<bool>();

        public IDbTransaction? DbTransaction { get; private set; }
        public IDbConnection? Connection { get; private set; }

        public void AddTransaction(IDbTransaction dbTransaction, IDbConnection connection)
        {
            DbTransaction = dbTransaction ?? throw new ArgumentNullException(nameof(dbTransaction));
            Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        public IDbTransaction? GetDbTransaction() => DbTransaction;

        public IDbConnection? GetConnection() => Connection;

        public void Clear()
        {
            DbTransaction = null;
            Connection = null;
        }

        public async Task<(bool Executed, T Result)> TryExecuteInTransactionAsync<T>(
            Func<IDbConnection, IDbTransaction, Task<T>> action,
            CancellationToken cancellationToken = default)
        {
            if (action is null)
                throw new ArgumentNullException(nameof(action));

            if (Connection is null)
                return (false, default!);

            if (_heldByCurrentFlow.Value)
                return await RunAsync(action).ConfigureAwait(false);

            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Definido neste frame async: flui para "action" e é desfeito ao retornar.
                _heldByCurrentFlow.Value = true;
                return await RunAsync(action).ConfigureAwait(false);
            }
            finally
            {
                _heldByCurrentFlow.Value = false;
                _semaphore.Release();
            }
        }

        private async Task<(bool Executed, T Result)> RunAsync<T>(Func<IDbConnection, IDbTransaction, Task<T>> action)
        {
            // Relê após adquirir o semáforo: a transação pode ter terminado enquanto aguardávamos.
            var connection = Connection;
            var transaction = DbTransaction;

            if (connection is null || transaction is null)
                return (false, default!);

            var result = await action(connection, transaction).ConfigureAwait(false);
            return (true, result);
        }
    }
}

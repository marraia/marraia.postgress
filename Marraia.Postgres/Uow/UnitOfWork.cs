using Marraia.Postgres.Repositories.Interfaces;
using Marraia.Postgres.Uow.Interfaces;
using Npgsql;
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace Marraia.Postgres.Uow
{
    public class UnitOfWork : IPostgresUnitOfWork, IDisposable, IAsyncDisposable
    {
        private readonly IPostgresConnectionFactory _connectionFactory;
        private readonly ITransactionBase _transactionBase;
        private NpgsqlConnection? _connection;
        private NpgsqlTransaction? _dbTransaction;

        public UnitOfWork(IPostgresConnectionFactory connectionFactory,
                          ITransactionBase transactionBase)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _transactionBase = transactionBase ?? throw new ArgumentNullException(nameof(transactionBase));
        }

        /// <summary>Indica se há uma transação em andamento nesta unidade de trabalho.</summary>
        public bool HasActiveTransaction => _dbTransaction != null;

        public UnitOfWork BeginTransaction(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
        {
            EnsureNoActiveTransaction();

            var connection = _connectionFactory.CreateConnection();
            try
            {
                connection.Open();
                Register(connection, connection.BeginTransaction(isolationLevel));
            }
            catch
            {
                connection.Dispose();
                throw;
            }

            return this;
        }

        public async Task<UnitOfWork> BeginTransactionAsync(
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
            CancellationToken cancellationToken = default)
        {
            EnsureNoActiveTransaction();

            var connection = _connectionFactory.CreateConnection();
            try
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                var transaction = await connection.BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false);
                Register(connection, transaction);
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            return this;
        }

        public bool Commit()
        {
            if (_dbTransaction == null)
                return false;

            try
            {
                _dbTransaction.Commit();
                return true;
            }
            finally
            {
                // Devolve a conexão ao pool imediatamente, sem esperar o Dispose do escopo.
                CompleteTransaction();
            }
        }

        public async Task<bool> CommitAsync(CancellationToken cancellationToken = default)
        {
            if (_dbTransaction == null)
                return false;

            try
            {
                await _dbTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }
            finally
            {
                await CompleteTransactionAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Desfaz a transação ativa. Não faz nada se não houver transação (ex.: o Commit já
        /// foi tentado e falhou), para não mascarar a exceção original num bloco catch.
        /// </summary>
        public void Rollback()
        {
            if (_dbTransaction == null)
                return;

            try
            {
                _dbTransaction.Rollback();
            }
            finally
            {
                CompleteTransaction();
            }
        }

        /// <inheritdoc cref="Rollback"/>
        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            if (_dbTransaction == null)
                return;

            try
            {
                await _dbTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await CompleteTransactionAsync().ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeAsyncCore().ConfigureAwait(false);
            Dispose(false);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing)
                return;

            // Rede de segurança: sem Commit/Rollback (ex.: exceção no meio da transação),
            // desfaz e libera a conexão aqui em vez de segurá-la até o fim do escopo.
            if (_dbTransaction != null)
            {
                try { _dbTransaction.Rollback(); }
                catch { /* best effort: a conexão pode já estar quebrada */ }
            }

            CompleteTransaction();
        }

        protected virtual async ValueTask DisposeAsyncCore()
        {
            if (_dbTransaction != null)
            {
                try { await _dbTransaction.RollbackAsync().ConfigureAwait(false); }
                catch { /* best effort */ }
            }

            await CompleteTransactionAsync().ConfigureAwait(false);
        }

        private void EnsureNoActiveTransaction()
        {
            if (_dbTransaction != null)
                throw new InvalidOperationException(
                    "Já existe uma transação em andamento nesta UnitOfWork. Chame Commit(), Rollback() ou Dispose() antes de iniciar outra.");
        }

        private void Register(NpgsqlConnection connection, NpgsqlTransaction transaction)
        {
            _connection = connection;
            _dbTransaction = transaction;

            // Os repositórios reutilizam este par conexão/transação em vez de abrir outra conexão do pool.
            _transactionBase.AddTransaction(transaction, connection);
        }

        private void CompleteTransaction()
        {
            _transactionBase.Clear();

            _dbTransaction?.Dispose();
            _dbTransaction = null;

            _connection?.Dispose();
            _connection = null;
        }

        private async ValueTask CompleteTransactionAsync()
        {
            _transactionBase.Clear();

            if (_dbTransaction != null)
            {
                await _dbTransaction.DisposeAsync().ConfigureAwait(false);
                _dbTransaction = null;
            }

            if (_connection != null)
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
                _connection = null;
            }
        }
    }
}

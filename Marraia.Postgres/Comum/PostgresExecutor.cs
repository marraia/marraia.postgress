using Marraia.Postgres.Repositories.Interfaces;
using Marraia.Postgres.Uow.Interfaces;
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace Marraia.Postgres.Comum
{
    /// <summary>
    /// Estratégia única de execução usada pelos repositórios:
    /// <list type="number">
    /// <item>Com transação ativa (UnitOfWork): usa a conexão da transação (sempre o banco de escrita,
    /// inclusive para leituras, para enxergar o que a transação acabou de gravar), com acesso
    /// serializado pelo semáforo do <see cref="ITransactionBase"/>.</item>
    /// <item>Sem transação: abre uma conexão da factory informada só para esta operação
    /// e a descarta em seguida. Operações paralelas recebem conexões distintas, limitadas pelo <c>Max Pool Size</c>.</item>
    /// </list>
    /// </summary>
    internal static class PostgresExecutor
    {
        public static async Task<T> ExecuteAsync<T>(
            IPostgresConnectionFactory connectionFactory,
            ITransactionBase? transactionBase,
            Func<IDbConnection, IDbTransaction?, Task<T>> action,
            CancellationToken cancellationToken)
        {
            if (action is null)
                throw new ArgumentNullException(nameof(action));

            if (transactionBase != null)
            {
                var (executed, result) = await transactionBase
                    .TryExecuteInTransactionAsync<T>((connection, transaction) => action(connection, transaction), cancellationToken)
                    .ConfigureAwait(false);

                if (executed)
                    return result;
            }

            var ownConnection = connectionFactory.CreateConnection();
            await using (ownConnection.ConfigureAwait(false))
            {
                await ownConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
                return await action(ownConnection, null).ConfigureAwait(false);
            }
        }

        public static Func<IDbConnection, IDbTransaction?, Task<bool>> ToGeneric(Func<IDbConnection, IDbTransaction?, Task> action)
        {
            if (action is null) throw new ArgumentNullException(nameof(action));
            return async (connection, transaction) =>
            {
                await action(connection, transaction).ConfigureAwait(false);
                return true;
            };
        }

        public static Func<IDbConnection, IDbTransaction?, Task<T>> IgnoreTransaction<T>(Func<IDbConnection, Task<T>> action)
        {
            if (action is null) throw new ArgumentNullException(nameof(action));
            return (connection, _) => action(connection);
        }

        public static Func<IDbConnection, IDbTransaction?, Task> IgnoreTransaction(Func<IDbConnection, Task> action)
        {
            if (action is null) throw new ArgumentNullException(nameof(action));
            return (connection, _) => action(connection);
        }
    }

    /// <summary>
    /// Resolve qual factory atende leituras e escritas de um repositório, conforme o construtor usado.
    /// </summary>
    internal readonly struct ConnectionRouting
    {
        private ConnectionRouting(IPostgresConnectionFactory read, IPostgresConnectionFactory write, bool segregated)
        {
            Read = read;
            Write = write;
            IsSegregated = segregated;
        }

        public IPostgresConnectionFactory Read { get; }
        public IPostgresConnectionFactory Write { get; }
        public bool IsSegregated { get; }

        public static ConnectionRouting Single(IPostgresConnectionFactory factory)
        {
            if (factory is null) throw new ArgumentNullException(nameof(factory));
            return new ConnectionRouting(factory, factory, false);
        }

        public static ConnectionRouting Segregated(IPostgresReadConnectionFactory read, IPostgresWriteConnectionFactory write)
        {
            if (read is null) throw new ArgumentNullException(nameof(read));
            if (write is null) throw new ArgumentNullException(nameof(write));
            return new ConnectionRouting(read, write, true);
        }
    }
}

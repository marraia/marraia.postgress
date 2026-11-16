using Marraia.Postgres.Comum;
using Marraia.Postgres.Core;
using Marraia.Postgres.Repositories.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace Marraia.Postgres.Repositories
{
    /// <summary>
    /// Repositório para consultas próprias com Dapper. Dois modos, escolhidos pelo construtor:
    /// <list type="bullet">
    /// <item><b>Padrão</b> (<see cref="IPostgresConnectionFactory"/>): use <c>ExecuteSafeAsync</c>.</item>
    /// <item><b>Leitura/escrita segregadas</b> (<see cref="IPostgresReadConnectionFactory"/> +
    /// <see cref="IPostgresWriteConnectionFactory"/>): use <c>ExecuteReadAsync</c> e <c>ExecuteWriteAsync</c>.</item>
    /// </list>
    /// Com uma transação ativa da UnitOfWork, todos usam a conexão da transação, serializando chamadas
    /// paralelas. Sem transação, cada operação abre uma conexão do pool só para si.
    /// </summary>
    /// <remarks>
    /// Não chame <c>Open</c>/<c>Close</c>/<c>Dispose</c> na conexão recebida. Dentro de uma transação,
    /// chamadas paralelas ao banco disparadas de dentro da própria action não são serializadas.
    /// </remarks>
    public class RepositoryStandard<TEntity, TKey> : CommonConfiguration<TEntity>
            where TEntity : Entity<TKey>
            where TKey : struct
    {
        private readonly ConnectionRouting _routing;
        private readonly DirectConnections _direct;
        protected readonly IPostgresTransactionBase _transactionBase;
        protected readonly ILogger? _logger;

        /// <summary>Modo padrão: uma única conexão (use <c>ExecuteSafeAsync</c>).</summary>
        protected RepositoryStandard(IPostgresConnectionFactory connectionFactory,
                                     IPostgresTransactionBase transactionBase,
                                     IConfiguration configuration,
                                     ILogger? logger = null)
            : base(configuration)
        {
            _routing = ConnectionRouting.Single(connectionFactory);
            _transactionBase = transactionBase ?? throw new ArgumentNullException(nameof(transactionBase));
            _logger = logger;
            _direct = new DirectConnections(_routing, _transactionBase);
        }

        /// <summary>
        /// Modo segregado: requer <c>AddPostgres(connectionStringRead, connectionStringWrite)</c>
        /// (use <c>ExecuteReadAsync</c> / <c>ExecuteWriteAsync</c>).
        /// </summary>
        protected RepositoryStandard(IPostgresReadConnectionFactory readConnectionFactory,
                                     IPostgresWriteConnectionFactory writeConnectionFactory,
                                     IPostgresTransactionBase transactionBase,
                                     IConfiguration configuration,
                                     ILogger? logger = null)
            : base(configuration)
        {
            _routing = ConnectionRouting.Segregated(readConnectionFactory, writeConnectionFactory);
            _transactionBase = transactionBase ?? throw new ArgumentNullException(nameof(transactionBase));
            _logger = logger;
            _direct = new DirectConnections(_routing, _transactionBase);
        }

        /// <summary>Indica se o repositório foi construído com leitura e escrita segregadas.</summary>
        protected bool IsReadWriteSegregated => _routing.IsSegregated;

        /// <summary>
        /// Conexão para uso direto com Dapper em chamadas <b>sequenciais</b> (no modo segregado, a de escrita).
        /// Com transação ativa é a conexão da transação; sem transação é uma conexão fechada que o Dapper
        /// abre e fecha a cada comando. Não chame <c>Open</c>/<c>Close</c>/<c>Dispose</c> nela.
        /// Para chamadas paralelas ou várias operações na mesma sessão, use <c>ExecuteSafeAsync</c>.
        /// </summary>
        protected IDbConnection _connection => _direct.Write;

        /// <summary>
        /// Igual a <see cref="_connection"/>, mas no modo segregado aponta para o banco de leitura
        /// (dentro de transação, continua sendo a conexão da transação).
        /// </summary>
        protected IDbConnection _readConnection => _direct.Read;

        /// <summary>Transação ativa da UnitOfWork, ou <c>null</c>.</summary>
        protected IDbTransaction? _transaction => _transactionBase.GetDbTransaction();

        #region Modo padrão

        /// <summary>
        /// Executa <paramref name="action"/> recebendo a conexão e a transação ativa (ou <c>null</c>).
        /// Repasse a transação ao Dapper: <c>conn.QueryAsync(sql, param, tx)</c>.
        /// No modo segregado, usa o banco de escrita.
        /// </summary>
        protected Task<T> ExecuteSafeAsync<T>(Func<IDbConnection, IDbTransaction?, Task<T>> action,
                                              CancellationToken cancellationToken = default)
            => Run(_routing.Write, action, cancellationToken);

        /// <inheritdoc cref="ExecuteSafeAsync{T}(Func{IDbConnection, IDbTransaction, Task{T}}, CancellationToken)"/>
        protected Task ExecuteSafeAsync(Func<IDbConnection, IDbTransaction?, Task> action,
                                        CancellationToken cancellationToken = default)
            => Run(_routing.Write, PostgresExecutor.ToGeneric(action), cancellationToken);

        /// <summary>
        /// Variante sem o parâmetro de transação. Dentro de uma UnitOfWork também usa a conexão da
        /// transação: no Npgsql todo comando da conexão roda na transação corrente, e usar outra
        /// conexão poderia travar em linhas bloqueadas pela própria transação.
        /// </summary>
        protected Task<T> ExecuteSafeAsync<T>(Func<IDbConnection, Task<T>> action,
                                              CancellationToken cancellationToken = default)
            => Run(_routing.Write, PostgresExecutor.IgnoreTransaction(action), cancellationToken);

        /// <inheritdoc cref="ExecuteSafeAsync{T}(Func{IDbConnection, Task{T}}, CancellationToken)"/>
        protected Task ExecuteSafeAsync(Func<IDbConnection, Task> action,
                                        CancellationToken cancellationToken = default)
            => Run(_routing.Write, PostgresExecutor.ToGeneric(PostgresExecutor.IgnoreTransaction(action)), cancellationToken);

        #endregion

        #region Modo segregado (leitura / escrita)

        /// <summary>
        /// Executa no banco de leitura (réplica). Fora do modo segregado, usa a conexão única.
        /// </summary>
        protected Task<T> ExecuteReadAsync<T>(Func<IDbConnection, IDbTransaction?, Task<T>> action,
                                              CancellationToken cancellationToken = default)
            => Run(_routing.Read, action, cancellationToken);

        /// <inheritdoc cref="ExecuteReadAsync{T}(Func{IDbConnection, IDbTransaction, Task{T}}, CancellationToken)"/>
        protected Task<T> ExecuteReadAsync<T>(Func<IDbConnection, Task<T>> action,
                                              CancellationToken cancellationToken = default)
            => Run(_routing.Read, PostgresExecutor.IgnoreTransaction(action), cancellationToken);

        /// <summary>
        /// Executa no banco de escrita (primário). Fora do modo segregado, usa a conexão única.
        /// </summary>
        protected Task<T> ExecuteWriteAsync<T>(Func<IDbConnection, IDbTransaction?, Task<T>> action,
                                               CancellationToken cancellationToken = default)
            => Run(_routing.Write, action, cancellationToken);

        /// <inheritdoc cref="ExecuteWriteAsync{T}(Func{IDbConnection, IDbTransaction, Task{T}}, CancellationToken)"/>
        protected Task ExecuteWriteAsync(Func<IDbConnection, IDbTransaction?, Task> action,
                                         CancellationToken cancellationToken = default)
            => Run(_routing.Write, PostgresExecutor.ToGeneric(action), cancellationToken);

        /// <inheritdoc cref="ExecuteWriteAsync{T}(Func{IDbConnection, IDbTransaction, Task{T}}, CancellationToken)"/>
        protected Task<T> ExecuteWriteAsync<T>(Func<IDbConnection, Task<T>> action,
                                               CancellationToken cancellationToken = default)
            => Run(_routing.Write, PostgresExecutor.IgnoreTransaction(action), cancellationToken);

        /// <inheritdoc cref="ExecuteWriteAsync{T}(Func{IDbConnection, IDbTransaction, Task{T}}, CancellationToken)"/>
        protected Task ExecuteWriteAsync(Func<IDbConnection, Task> action,
                                         CancellationToken cancellationToken = default)
            => Run(_routing.Write, PostgresExecutor.ToGeneric(PostgresExecutor.IgnoreTransaction(action)), cancellationToken);

        #endregion

        private async Task<T> Run<T>(IPostgresConnectionFactory factory,
                                     Func<IDbConnection, IDbTransaction?, Task<T>> action,
                                     CancellationToken cancellationToken)
        {
            try
            {
                return await PostgresExecutor
                    .ExecuteAsync(factory, _transactionBase, action, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (_logger != null && !(ex is OperationCanceledException))
            {
                _logger.LogError(ex, "Erro ao executar operação no Postgres para {Entity}", typeof(TEntity).Name);
                throw;
            }
        }
    }
}

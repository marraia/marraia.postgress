using Dapper;
using Marraia.Postgres.Comum;
using Marraia.Postgres.Core;
using Marraia.Postgres.Repositories.Interfaces;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace Marraia.Postgres.Repositories
{
    /// <summary>
    /// Repositório com CRUD pronto (Insert/Update/Delete/GetById/GetAll).
    /// <list type="bullet">
    /// <item>Construtor com <see cref="IPostgresConnectionFactory"/>: uma única conexão para tudo.</item>
    /// <item>Construtor com <see cref="IPostgresReadConnectionFactory"/> e <see cref="IPostgresWriteConnectionFactory"/>:
    /// GetById/GetAll vão para a leitura e Insert/Update/Delete para a escrita.</item>
    /// </list>
    /// Com uma transação ativa da UnitOfWork, todas as operações usam a conexão da transação.
    /// </summary>
    public abstract class RepositoryBase<TEntity, TKey> : CommonConfiguration<TEntity>, IRepositoryBase<TEntity, TKey>
                where TEntity : Entity<TKey>
                where TKey : struct
    {
        private readonly ConnectionRouting _routing;
        private readonly DirectConnections _direct;
        protected readonly IPostgresTransactionBase _transactionBase;

        /// <summary>Modo padrão: uma única conexão para leitura e escrita.</summary>
        protected RepositoryBase(IPostgresConnectionFactory connectionFactory,
                                 IPostgresTransactionBase transactionBase,
                                 IConfiguration configuration)
            : base(configuration)
        {
            _routing = ConnectionRouting.Single(connectionFactory);
            _transactionBase = transactionBase ?? throw new ArgumentNullException(nameof(transactionBase));
            _direct = new DirectConnections(_routing, _transactionBase);
        }

        /// <summary>Modo segregado: leituras na réplica, comandos no primário.</summary>
        protected RepositoryBase(IPostgresReadConnectionFactory readConnectionFactory,
                                 IPostgresWriteConnectionFactory writeConnectionFactory,
                                 IPostgresTransactionBase transactionBase,
                                 IConfiguration configuration)
            : base(configuration)
        {
            _routing = ConnectionRouting.Segregated(readConnectionFactory, writeConnectionFactory);
            _transactionBase = transactionBase ?? throw new ArgumentNullException(nameof(transactionBase));
            _direct = new DirectConnections(_routing, _transactionBase);
        }

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

        public virtual Task<TKey> InsertAsync(TEntity entity)
        {
            var sql = GenerateInsertQuery();

            return Execute(_routing.Write, (connection, transaction) =>
                connection.ExecuteScalarAsync<TKey>(new CommandDefinition(sql, entity, transaction)));
        }

        public virtual Task UpdateAsync(TEntity entity)
        {
            var sql = GenerateUpdateQuery();
            if (string.IsNullOrEmpty(sql))
                throw new InvalidOperationException($"A entidade {typeof(TEntity).Name} não possui colunas para atualizar.");

            return Execute(_routing.Write, (connection, transaction) =>
                connection.ExecuteAsync(new CommandDefinition(sql, entity, transaction)));
        }

        public virtual Task DeleteAsync(TKey id)
        {
            var sql = GenerateDeleteQuery();

            return Execute(_routing.Write, (connection, transaction) =>
                connection.ExecuteAsync(new CommandDefinition(sql, new { Id = id }, transaction)));
        }

        public virtual Task<TEntity?> GetByIdAsync(TKey id)
        {
            var sql = GenerateSelectByIdQuery();

            return Execute(_routing.Read, (connection, transaction) =>
                connection.QuerySingleOrDefaultAsync<TEntity?>(new CommandDefinition(sql, new { Id = id }, transaction)));
        }

        public virtual Task<IEnumerable<TEntity>> GetAllAsync()
        {
            var sql = GenerateSelectAllQuery();

            return Execute(_routing.Read, (connection, transaction) =>
                connection.QueryAsync<TEntity>(new CommandDefinition(sql, transaction: transaction)));
        }

        /// <summary>
        /// Executa <paramref name="action"/> participando da transação ativa (quando houver) ou numa conexão
        /// própria do pool. Use em sobrescritas e métodos customizados. No modo segregado, usa a escrita.
        /// </summary>
        protected Task<T> ExecuteSafeAsync<T>(Func<IDbConnection, IDbTransaction?, Task<T>> action,
                                              CancellationToken cancellationToken = default)
            => Execute(_routing.Write, action, cancellationToken);

        /// <inheritdoc cref="ExecuteSafeAsync{T}(Func{IDbConnection, IDbTransaction, Task{T}}, CancellationToken)"/>
        protected Task ExecuteSafeAsync(Func<IDbConnection, IDbTransaction?, Task> action,
                                        CancellationToken cancellationToken = default)
            => Execute(_routing.Write, PostgresExecutor.ToGeneric(action), cancellationToken);

        /// <summary>Modo segregado: executa no banco de leitura. No modo padrão, usa a conexão única.</summary>
        protected Task<T> ExecuteReadAsync<T>(Func<IDbConnection, IDbTransaction?, Task<T>> action,
                                              CancellationToken cancellationToken = default)
            => Execute(_routing.Read, action, cancellationToken);

        /// <summary>Modo segregado: executa no banco de escrita. No modo padrão, usa a conexão única.</summary>
        protected Task<T> ExecuteWriteAsync<T>(Func<IDbConnection, IDbTransaction?, Task<T>> action,
                                               CancellationToken cancellationToken = default)
            => Execute(_routing.Write, action, cancellationToken);

        /// <inheritdoc cref="ExecuteWriteAsync{T}(Func{IDbConnection, IDbTransaction, Task{T}}, CancellationToken)"/>
        protected Task ExecuteWriteAsync(Func<IDbConnection, IDbTransaction?, Task> action,
                                         CancellationToken cancellationToken = default)
            => Execute(_routing.Write, PostgresExecutor.ToGeneric(action), cancellationToken);

        private Task<T> Execute<T>(IPostgresConnectionFactory factory,
                                   Func<IDbConnection, IDbTransaction?, Task<T>> action,
                                   CancellationToken cancellationToken = default)
            => PostgresExecutor.ExecuteAsync(factory, _transactionBase, action, cancellationToken);
    }
}

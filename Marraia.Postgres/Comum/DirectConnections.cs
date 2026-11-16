using Marraia.Postgres.Uow.Interfaces;
using Npgsql;
using System.Data;

namespace Marraia.Postgres.Comum
{
    /// <summary>
    /// Conexões para uso direto com Dapper (sem <c>ExecuteSafeAsync</c>):
    /// <list type="bullet">
    /// <item>Com transação ativa: a conexão da transação (o Npgsql executa todo comando dela na transação).</item>
    /// <item>Sem transação: uma conexão <b>fechada</b> do repositório. O Dapper abre a conexão, executa e
    /// fecha a cada chamada, devolvendo-a ao pool; por isso ela nunca fica presa entre operações.</item>
    /// </list>
    /// </summary>
    internal sealed class DirectConnections
    {
        private readonly ConnectionRouting _routing;
        private readonly ITransactionBase _transactionBase;
        private NpgsqlConnection? _write;
        private NpgsqlConnection? _read;

        public DirectConnections(ConnectionRouting routing, ITransactionBase transactionBase)
        {
            _routing = routing;
            _transactionBase = transactionBase;
        }

        public IDbConnection Write
            => _transactionBase.GetConnection() ?? (_write ??= _routing.Write.CreateConnection());

        public IDbConnection Read
        {
            get
            {
                var transactional = _transactionBase.GetConnection();
                if (transactional != null)
                    return transactional;

                return _routing.IsSegregated
                    ? (_read ??= _routing.Read.CreateConnection())
                    : Write;
            }
        }
    }
}

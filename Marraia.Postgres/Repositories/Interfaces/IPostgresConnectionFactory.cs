using Npgsql;

namespace Marraia.Postgres.Repositories.Interfaces
{
    /// <summary>
    /// Cria conexões PostgreSQL a partir de um <see cref="NpgsqlDataSource"/> (pool compartilhado).
    /// É a conexão padrão, registrada por <c>AddPostgres</c> (nos dois modos). Cada conexão criada deve
    /// ser aberta, usada e descartada pela própria operação: o descarte apenas a devolve ao pool.
    /// </summary>
    public interface IPostgresConnectionFactory
    {
        /// <summary>
        /// Cria uma nova conexão (fechada). Quem chama é responsável por abri-la e descartá-la.
        /// </summary>
        NpgsqlConnection CreateConnection();
    }

    /// <summary>
    /// Conexões com o banco de <b>leitura</b> (réplica). Registrado apenas por
    /// <c>AddPostgres(connectionStringRead, connectionStringWrite)</c>.
    /// </summary>
    public interface IPostgresReadConnectionFactory : IPostgresConnectionFactory { }

    /// <summary>
    /// Conexões com o banco de <b>escrita</b> (primário). Registrado apenas por
    /// <c>AddPostgres(connectionStringRead, connectionStringWrite)</c>; é o mesmo pool
    /// entregue como <see cref="IPostgresConnectionFactory"/> nesse modo.
    /// </summary>
    public interface IPostgresWriteConnectionFactory : IPostgresConnectionFactory { }
}

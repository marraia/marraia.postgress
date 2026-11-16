using Marraia.Postgres.Repositories.Interfaces;
using Npgsql;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Marraia.Postgres.Configurations
{
    /// <summary>
    /// Implementação de <see cref="IPostgresConnectionFactory"/> baseada em <see cref="NpgsqlDataSource"/>.
    /// O data source é thread-safe e mantém o pool de conexões; a factory deve viver como Singleton.
    /// </summary>
    public class PostgresConnectionFactory : IPostgresConnectionFactory, IDisposable, IAsyncDisposable
    {
        private readonly NpgsqlDataSource _dataSource;
        private int _disposed;

        public PostgresConnectionFactory(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("A connection string do Postgres não pode ser vazia.", nameof(connectionString));

            _dataSource = NpgsqlDataSource.Create(connectionString);
        }

        public PostgresConnectionFactory(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        }

        public NpgsqlConnection CreateConnection() => _dataSource.CreateConnection();

        // Idempotente: no modo leitura/escrita a mesma instância é exposta por duas interfaces.
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _dataSource.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            return Interlocked.Exchange(ref _disposed, 1) == 0
                ? _dataSource.DisposeAsync()
                : default;
        }
    }

    /// <summary>Factory do banco de leitura (réplica).</summary>
    public sealed class PostgresReadConnectionFactory : PostgresConnectionFactory, IPostgresReadConnectionFactory
    {
        public PostgresReadConnectionFactory(string connectionString) : base(connectionString) { }
        public PostgresReadConnectionFactory(NpgsqlDataSource dataSource) : base(dataSource) { }
    }

    /// <summary>Factory do banco de escrita (primário).</summary>
    public sealed class PostgresWriteConnectionFactory : PostgresConnectionFactory, IPostgresWriteConnectionFactory
    {
        public PostgresWriteConnectionFactory(string connectionString) : base(connectionString) { }
        public PostgresWriteConnectionFactory(NpgsqlDataSource dataSource) : base(dataSource) { }
    }
}

using Marraia.Postgres.Repositories.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;

namespace Marraia.Postgres.Configurations
{
    public static class DapperConfigurationsExtensions
    {
        /// <summary>
        /// Registra o acesso ao Postgres com uma única connection string:
        /// <list type="bullet">
        /// <item><see cref="IPostgresConnectionFactory"/> (Singleton, pool via NpgsqlDataSource)</item>
        /// <item><see cref="IPostgresTransactionBase"/> (Scoped, transação corrente do request)</item>
        /// <item><see cref="IPostgresUnitOfWork"/> (Scoped)</item>
        /// </list>
        /// Os tipos com prefixo <c>IPostgres</c> permitem usar esta biblioteca junto com outras bibliotecas
        /// Marraia (ex.: Marraia.MySql) no mesmo projeto sem colisão de registros.
        /// </summary>
        public static IServiceCollection AddPostgres(this IServiceCollection services, string connectionString)
        {
            EnsureNotRegistered(services);

            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("A connection string do Postgres não pode ser vazia.", nameof(connectionString));

            // Registrado via factory (e não instância) para que o container descarte o NpgsqlDataSource.
            services.AddSingleton<IPostgresConnectionFactory>(_ => new PostgresConnectionFactory(connectionString));

            return AddUnitOfWork(services);
        }

        /// <summary>
        /// Registra o acesso ao Postgres com leitura e escrita segregadas. Além do registrado pela sobrecarga
        /// de uma connection string, disponibiliza:
        /// <list type="bullet">
        /// <item><see cref="IPostgresReadConnectionFactory"/>: banco de leitura (réplica).</item>
        /// <item><see cref="IPostgresWriteConnectionFactory"/>: banco de escrita (primário).</item>
        /// </list>
        /// <see cref="IPostgresConnectionFactory"/> e a UnitOfWork usam o banco de escrita, então repositórios
        /// que não optarem pela segregação continuam funcionando sem alteração.
        /// </summary>
        public static IServiceCollection AddPostgres(this IServiceCollection services,
                                                     string connectionStringRead,
                                                     string connectionStringWrite)
        {
            EnsureNotRegistered(services);

            if (string.IsNullOrWhiteSpace(connectionStringRead))
                throw new ArgumentException("A connection string de leitura do Postgres não pode ser vazia.", nameof(connectionStringRead));
            if (string.IsNullOrWhiteSpace(connectionStringWrite))
                throw new ArgumentException("A connection string de escrita do Postgres não pode ser vazia.", nameof(connectionStringWrite));

            services.AddSingleton<IPostgresReadConnectionFactory>(_ => new PostgresReadConnectionFactory(connectionStringRead));
            services.AddSingleton<IPostgresWriteConnectionFactory>(_ => new PostgresWriteConnectionFactory(connectionStringWrite));
            services.AddSingleton<IPostgresConnectionFactory>(sp => sp.GetRequiredService<IPostgresWriteConnectionFactory>());

            return AddUnitOfWork(services);
        }

        private static IServiceCollection AddUnitOfWork(IServiceCollection services)
        {
            services.AddScoped<IPostgresTransactionBase, PostgresTransactionBase>();
            services.AddScoped<IPostgresUnitOfWork, PostgresUnitOfWork>();
            return services;
        }

        private static void EnsureNotRegistered(IServiceCollection services)
        {
            if (services is null)
                throw new ArgumentNullException(nameof(services));

            if (services.Any(d => d.ServiceType == typeof(IPostgresConnectionFactory)))
                throw new InvalidOperationException(
                    "AddPostgres já foi chamado neste container. Para segregar leitura e escrita use " +
                    "AddPostgres(connectionStringRead, connectionStringWrite) em uma única chamada.");
        }
    }
}

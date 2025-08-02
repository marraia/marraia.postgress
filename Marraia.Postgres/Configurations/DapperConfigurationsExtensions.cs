using Marraia.Postgres.Repositories.Interfaces;
using Marraia.Postgres.Uow.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Marraia.Postgres.Configurations
{
    public interface IPostgresTransactionBase : ITransactionBase { }
    public static class DapperConfigurationsExtensions
    {
        public static IServiceCollection AddPostgres(this IServiceCollection service, string connectionString)
        {
            service.AddScoped<IPostgresDbConnection>(db => new PostgresDbConnection(connectionString));
            service.AddScoped<IPostgresUnitOfWork, PostgresUnitOfWork>();
            service.AddScoped<IPostgresTransactionBase, PostgresTransactionBase>();

            return service;
        }
    }
}

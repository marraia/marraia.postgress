using Marraia.Postgres.Uow;
using Marraia.Postgres.Uow.Interfaces;
using Marraia.Postgres.Uow.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using System.Data;

namespace Marraia.Postgres.Configurations
{
    public static class DapperConfigurationsExtensions
    {
        public static IServiceCollection AddDapperPostgres(this IServiceCollection service, string connectionString)
        {
            service.AddScoped<IDbConnection>(db => new NpgsqlConnection(connectionString));
            service.AddScoped<IUnitOfWork, UnitOfWork>();
            service.AddScoped<ITransactionBase, TransactionBase>();

            return service;
        }
    }
}

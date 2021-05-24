using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Marraia.Postgres.Comum
{
    public abstract class CommonConfiguration<TEntity>
               where TEntity : class
    {
        private const int RemoveCaracteres = 1;
        private IEnumerable<PropertyInfo> GetProperties => typeof(TEntity).GetProperties();
        protected readonly string Schema = "";

        public CommonConfiguration(IConfiguration configuration)
        {
            Schema = configuration.GetSection("SchemaBD").Value;
        }

        protected string GenerateUpdateQuery()
        {
            var query = new StringBuilder($"UPDATE {Schema}.{typeof(TEntity).Name} SET ");
            var properties = GetPropertiesByEntity(GetProperties);

            properties.ForEach(property =>
            {
                if (!property.Equals("Id"))
                {
                    query.Append($"{property}=@{property},");
                }
            });

            query.Remove(query.Length - RemoveCaracteres, RemoveCaracteres);
            query.Append(" WHERE Id=@Id");

            return query.ToString();
        }

        public string GenerateInsertQuery()
        {
            var query = new StringBuilder($"INSERT INTO {Schema}.{typeof(TEntity).Name} ");
            query.Append("(");

            var properties = GetPropertiesByEntity(GetProperties);

            properties.ForEach(property =>
            {
                if (!property.Equals("Id"))
                    query.Append($"[{property}],");
            });

            query
                .Remove(query.Length - RemoveCaracteres, RemoveCaracteres)
                .Append(") VALUES (");

            properties.ForEach(property =>
            {
                if (!property.Equals("Id"))
                    query.Append($"@{property},");
            });

            query
                .Remove(query.Length - RemoveCaracteres, RemoveCaracteres)
                .Append(");");

            query.Append($" SELECT currval(pg_get_serial_sequence('{Schema}.{typeof(TEntity).Name}','id'));");

            return query.ToString();
        }

        public string GenerateSelectByIdQuery()
        {
            var sql = $"SELECT * FROM {Schema}.{typeof(TEntity).Name} WHERE ID=@Id";

            return sql;
        }

        public string GenerateDeleteQuery()
        {
            var sql = $"DELETE FROM {Schema}.{typeof(TEntity).Name} WHERE ID=@Id";

            return sql;
        }

        public string GenerateSelectAllQuery()
        {
            var sql = $"SELECT * FROM {Schema}.{typeof(TEntity).Name}";

            return sql;
        }

        private static List<string> GetPropertiesByEntity(IEnumerable<PropertyInfo> properties)
        {
            return (from property in properties
                    let attributes = property.GetCustomAttributes(typeof(DescriptionAttribute), false)
                    where attributes.Length <= 0
                    select property.Name).ToList();
        }
    }
}

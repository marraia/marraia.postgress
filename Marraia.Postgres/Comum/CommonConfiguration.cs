using Marraia.Postgres.Attributes;
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
        protected readonly string Schema = string.Empty;
        protected readonly string NameTable = string.Empty;

        public CommonConfiguration(IConfiguration configuration)
        {
            Schema = configuration.GetSection("SchemaBD").Value;
            NameTable = GetNameTableDescriptionAnnotation();
        }

        protected string GenerateUpdateQuery()
        {
            var query = new StringBuilder($"UPDATE {Schema}.{NameTable} SET ");
            var properties = GetPropertiesByEntity(GetProperties);

            properties.ForEach(property =>
            {
                if (!property.Equals("Id"))
                {
                    if (!IsIgnore(property))
                    {
                        query.Append($"{property}=@{property},");
                    }
                }
            });

            query.Remove(query.Length - RemoveCaracteres, RemoveCaracteres);
            query.Append(" WHERE Id=@Id");

            return query.ToString();
        }

        public string GenerateInsertQuery()
        {
            var query = new StringBuilder($"INSERT INTO {Schema}.{NameTable} ");
            query.Append("(");

            var properties = GetPropertiesByEntity(GetProperties);

            properties.ForEach(property =>
            {
                if (HasPropertyCommand(property, GetProperties))
                {
                    if (!IsIgnore(property))
                    {
                        query.Append($"{property},");
                    }
                }
            });

            query
                .Remove(query.Length - RemoveCaracteres, RemoveCaracteres)
                .Append(") VALUES (");

            properties.ForEach(property =>
            {
                if (HasPropertyCommand(property, GetProperties))
                {
                    if (!IsIgnore(property))
                    {
                        query.Append($"@{property},");
                    }
                }
            });

            query
                .Remove(query.Length - RemoveCaracteres, RemoveCaracteres)
                .Append(");");

            query.Append($" SELECT currval(pg_get_serial_sequence('{Schema}.{NameTable}','id'));");

            return query.ToString();
        }

        public string GenerateSelectByIdQuery()
        {
            var sql = $"SELECT * FROM {Schema}.{NameTable} WHERE Id=@Id";

            return sql;
        }

        public string GenerateDeleteQuery()
        {
            var sql = $"DELETE FROM {Schema}.{NameTable} WHERE Id=@Id";

            return sql;
        }

        public string GenerateSelectAllQuery()
        {
            var sql = $"SELECT * FROM {Schema}.{NameTable}";

            return sql;
        }

        private string GetNameTableDescriptionAnnotation()
        {
            var attributes = typeof(TEntity);
            var annotation = attributes.Name;

            var descriptions = (DescriptionAttribute[])
                                    attributes.GetCustomAttributes(typeof(DescriptionAttribute), false);

            if (descriptions != null)
            {
                annotation = descriptions
                                .FirstOrDefault()
                                .Description;
            }

            return annotation;
        }

        private bool IsIgnore(string nameProperty)
        {
            var attributes = typeof(TEntity);
            var propertyInfo = attributes.GetProperty(nameProperty);

            return Attribute
                       .IsDefined(propertyInfo, typeof(IgnorePropertyAttribute));
        }

        private static List<string> GetPropertiesByEntity(IEnumerable<PropertyInfo> properties)
        {
            return (from property in properties
                    let attributes = property.GetCustomAttributes(typeof(DescriptionAttribute), false)
                    where attributes.Length <= 0
                    select property.Name).ToList();
        }

        private bool HasPropertyCommand(string propertyItem, IEnumerable<PropertyInfo> properties)
        {
            var hasCommand = true;

            if (propertyItem == "Id")
            {
                var property = properties
                                .Where(prop => prop.Name == propertyItem)
                                .FirstOrDefault();

                if (property != null)
                {
                    if (property.PropertyType == typeof(Guid))
                        hasCommand = true;
                    else
                        hasCommand = false;
                }
            }

            return hasCommand;
        }
    }
}

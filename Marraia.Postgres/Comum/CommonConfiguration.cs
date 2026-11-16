using Marraia.Postgres.Attributes;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace Marraia.Postgres.Comum
{
    /// <summary>
    /// Gera o SQL de CRUD da entidade por reflexão. Os metadados são lidos uma única vez por tipo
    /// e o SQL é gerado uma única vez por par (entidade, schema).
    /// <para>Convenções:</para>
    /// <list type="bullet">
    /// <item>Tabela: <see cref="DescriptionAttribute"/> na classe, ou o nome da classe.</item>
    /// <item>Schema: chave <c>SchemaBD</c> do <see cref="IConfiguration"/> (opcional).</item>
    /// <item>Propriedades com <see cref="IgnorePropertyAttribute"/> ou <see cref="DescriptionAttribute"/> não são persistidas.</item>
    /// <item>Chave <c>Id</c>: incluída no INSERT apenas quando é <see cref="Guid"/> (as demais são geradas pelo banco).</item>
    /// </list>
    /// </summary>
    public abstract class CommonConfiguration<TEntity>
               where TEntity : class
    {
        private const string KeyColumn = "Id";
        private static readonly ConcurrentDictionary<string, SqlStatements> StatementsBySchema =
            new ConcurrentDictionary<string, SqlStatements>(StringComparer.Ordinal);

        protected readonly string? Schema;
        protected readonly string NameTable;

        private readonly SqlStatements _statements;

        protected CommonConfiguration(IConfiguration? configuration)
        {
            var schema = configuration?.GetSection("SchemaBD").Value;
            Schema = string.IsNullOrWhiteSpace(schema) ? null : schema!.Trim();
            NameTable = EntityMetadata.TableName;
            _statements = StatementsBySchema.GetOrAdd(Schema ?? string.Empty, s => new SqlStatements(s));
        }

        /// <summary>Nome qualificado da tabela (<c>schema.tabela</c>, ou só <c>tabela</c> sem schema).</summary>
        protected string FullTableName => _statements.FullTableName;

        protected string GenerateUpdateQuery() => _statements.Update;

        /// <summary>Gera um UPDATE apenas com as colunas informadas (a chave <c>Id</c> é ignorada no SET).</summary>
        protected string GenerateUpdateQuery(IEnumerable<string> properties)
        {
            if (properties is null)
                throw new ArgumentNullException(nameof(properties));

            var columns = properties
                .Where(p => !string.Equals(p, KeyColumn, StringComparison.Ordinal))
                .Select(p => $"{p}=@{p}")
                .ToList();

            if (columns.Count == 0)
                throw new ArgumentException("Informe ao menos uma coluna além de Id.", nameof(properties));

            return $"UPDATE {FullTableName} SET {string.Join(",", columns)} WHERE {KeyColumn}=@{KeyColumn}";
        }

        public string GenerateInsertQuery() => _statements.Insert;

        public string GenerateSelectByIdQuery() => _statements.SelectById;

        public string GenerateDeleteQuery() => _statements.Delete;

        public string GenerateSelectAllQuery() => _statements.SelectAll;

        private sealed class SqlStatements
        {
            public SqlStatements(string schema)
            {
                FullTableName = string.IsNullOrEmpty(schema)
                    ? EntityMetadata.TableName
                    : $"{schema}.{EntityMetadata.TableName}";

                var insertColumns = EntityMetadata.InsertColumns;
                Insert = insertColumns.Count == 0
                    ? $"INSERT INTO {FullTableName} DEFAULT VALUES RETURNING {KeyColumn}"
                    : $"INSERT INTO {FullTableName} ({string.Join(",", insertColumns)}) " +
                      $"VALUES ({string.Join(",", insertColumns.Select(c => "@" + c))}) RETURNING {KeyColumn}";

                var updateColumns = EntityMetadata.UpdateColumns;
                Update = updateColumns.Count == 0
                    ? string.Empty
                    : $"UPDATE {FullTableName} SET {string.Join(",", updateColumns.Select(c => $"{c}=@{c}"))} WHERE {KeyColumn}=@{KeyColumn}";

                SelectById = $"SELECT * FROM {FullTableName} WHERE {KeyColumn}=@{KeyColumn}";
                Delete = $"DELETE FROM {FullTableName} WHERE {KeyColumn}=@{KeyColumn}";
                SelectAll = $"SELECT * FROM {FullTableName}";
            }

            public string FullTableName { get; }
            public string Insert { get; }
            public string Update { get; }
            public string SelectById { get; }
            public string Delete { get; }
            public string SelectAll { get; }
        }

        private static class EntityMetadata
        {
            public static readonly string TableName = ResolveTableName();
            public static readonly IReadOnlyList<string> InsertColumns;
            public static readonly IReadOnlyList<string> UpdateColumns;

            static EntityMetadata()
            {
                var persisted = typeof(TEntity)
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanRead
                             && p.GetIndexParameters().Length == 0
                             && !p.IsDefined(typeof(IgnorePropertyAttribute), true)
                             && !p.IsDefined(typeof(DescriptionAttribute), false))
                    // Propriedades redeclaradas com "new" aparecem duplicadas: mantém a mais derivada.
                    .GroupBy(p => p.Name, StringComparer.Ordinal)
                    .Select(g => g.OrderByDescending(p => Depth(p.DeclaringType)).First())
                    .ToList();

                var key = persisted.FirstOrDefault(p => p.Name == KeyColumn);
                var keyIsGuid = key != null && (key.PropertyType == typeof(Guid) || key.PropertyType == typeof(Guid?));

                InsertColumns = persisted
                    .Where(p => p.Name != KeyColumn || keyIsGuid)
                    .Select(p => p.Name)
                    .ToList();

                UpdateColumns = persisted
                    .Where(p => p.Name != KeyColumn)
                    .Select(p => p.Name)
                    .ToList();
            }

            private static string ResolveTableName()
            {
                var description = typeof(TEntity).GetCustomAttribute<DescriptionAttribute>(false)?.Description;
                return string.IsNullOrWhiteSpace(description) ? typeof(TEntity).Name : description!;
            }

            private static int Depth(Type? type)
            {
                var depth = 0;
                for (var t = type; t != null; t = t.BaseType) depth++;
                return depth;
            }
        }
    }
}

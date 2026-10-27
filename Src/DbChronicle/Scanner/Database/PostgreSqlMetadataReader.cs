using System;
using System.Collections.Generic;

using DbChronicle.Common.Enums;
using DbChronicle.Execution;
using DbChronicle.Models;
using DbChronicle.Models.Operations;

namespace DbChronicle.Scanner.Database
{
    /// <summary>PostgreSQL 元数据读取：pg_class / pg_attribute / pg_index / pg_constraint。</summary>
    public sealed class PostgreSqlMetadataReader : MetadataReaderBase
    {
        public override string DialectName => "postgresql";

        public override ScanResult Read(IDbExecutor executor, ScanDatabaseOptions options)
        {
            var result = new ScanResult();
            var byTable = new Dictionary<string, ScannedTable>(StringComparer.OrdinalIgnoreCase);

            var tables = executor.Query(
                "SELECT c.relname AS table_name, obj_description(c.oid, 'pg_class') AS comment "
                + "FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace "
                + "WHERE c.relkind = 'r' AND n.nspname = current_schema() ORDER BY c.relname");

            foreach (var row in tables)
            {
                var name = Str(row, "table_name");
                if (string.IsNullOrEmpty(name) || !options.ShouldInclude(name))
                {
                    continue;
                }

                var table = new ScannedTable
                {
                    Name = name,
                    Comment = options.IncludeComments ? NullIfEmpty(Str(row, "comment")) : null
                };
                byTable[name] = table;
                result.Tables.Add(table);
            }

            if (byTable.Count == 0)
            {
                return result;
            }

            ReadColumns(executor, byTable, options);
            ReadPrimaryKeys(executor, byTable);
            if (options.IncludeIndexes)
            {
                ReadIndexes(executor, byTable);
            }
            if (options.IncludeForeignKeys)
            {
                ReadForeignKeys(executor, byTable);
            }
            if (options.IncludeCheckConstraints)
            {
                ReadCheckConstraints(executor, byTable);
            }

            return result;
        }

        private void ReadColumns(IDbExecutor executor, Dictionary<string, ScannedTable> byTable, ScanDatabaseOptions options)
        {
            var rows = executor.Query(
                "SELECT c.relname AS table_name, a.attname AS column_name, t.typname AS base_type, "
                + "format_type(a.atttypid, a.atttypmod) AS column_type, a.attnotnull AS not_null, "
                + "pg_get_expr(d.adbin, d.adrelid) AS column_default, col_description(c.oid, a.attnum) AS comment "
                + "FROM pg_class c "
                + "JOIN pg_namespace n ON n.oid = c.relnamespace "
                + "JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped "
                + "JOIN pg_type t ON t.oid = a.atttypid "
                + "LEFT JOIN pg_attrdef d ON d.adrelid = c.oid AND d.adnum = a.attnum "
                + "WHERE c.relkind = 'r' AND n.nspname = current_schema() "
                + "ORDER BY c.relname, a.attnum");

            foreach (var row in rows)
            {
                var tableName = Str(row, "table_name");
                ScannedTable table;
                if (string.IsNullOrEmpty(tableName) || !byTable.TryGetValue(tableName, out table))
                {
                    continue;
                }

                var physical = PhysicalType.Parse(Str(row, "column_type") ?? "");
                var column = new ScannedColumn
                {
                    Name = Str(row, "column_name") ?? "",
                    Nullable = !Truthy(row, "not_null"),
                    DefaultValue = NormalizeDefault(Str(row, "column_default")),
                    Comment = options.IncludeComments ? NullIfEmpty(Str(row, "comment")) : null
                };

                column.Length = physical.First;
                if (!physical.Second.HasValue && physical.First.HasValue && physical.BaseName.StartsWith("numeric", StringComparison.OrdinalIgnoreCase))
                {
                    column.Precision = physical.First;
                }
                else if (physical.Second.HasValue)
                {
                    column.Precision = physical.First;
                    column.Scale = physical.Second;
                }

                MapType(Str(row, "base_type") ?? "", column);
                table.Columns.Add(column);
            }
        }

        private void ReadPrimaryKeys(IDbExecutor executor, Dictionary<string, ScannedTable> byTable)
        {
            var rows = executor.Query(
                "SELECT c.relname AS table_name, a.attname AS column_name "
                + "FROM pg_index i "
                + "JOIN pg_class c ON c.oid = i.indrelid "
                + "JOIN pg_namespace n ON n.oid = c.relnamespace "
                + "JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum = ANY(i.indkey) "
                + "WHERE i.indisprimary AND n.nspname = current_schema() "
                + "ORDER BY c.relname, array_position(i.indkey, a.attnum)");

            foreach (var row in rows)
            {
                var tableName = Str(row, "table_name");
                ScannedTable table;
                if (string.IsNullOrEmpty(tableName) || !byTable.TryGetValue(tableName, out table))
                {
                    continue;
                }

                var columnName = Str(row, "column_name");
                if (string.IsNullOrEmpty(columnName) || table.PrimaryKeyColumns.Contains(columnName))
                {
                    continue;
                }

                table.PrimaryKeyColumns.Add(columnName);
                foreach (var column in table.Columns)
                {
                    if (string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase))
                    {
                        column.PrimaryKey = true;
                        column.Nullable = false;
                        if (column.DefaultValue != null
                            && column.DefaultValue.IndexOf("nextval", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            column.AutoIncrement = true;
                            column.DefaultValue = null;
                        }
                    }
                }
            }
        }

        private void ReadIndexes(IDbExecutor executor, Dictionary<string, ScannedTable> byTable)
        {
            var rows = executor.Query(
                "SELECT t.relname AS table_name, i.relname AS index_name, ix.indisunique AS is_unique, "
                + "a.attname AS column_name "
                + "FROM pg_index ix "
                + "JOIN pg_class i ON i.oid = ix.indexrelid "
                + "JOIN pg_class t ON t.oid = ix.indrelid "
                + "JOIN pg_namespace n ON n.oid = t.relnamespace "
                + "LEFT JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = ANY(ix.indkey) "
                + "WHERE NOT ix.indisprimary AND n.nspname = current_schema() "
                + "ORDER BY t.relname, i.relname, array_position(ix.indkey, a.attnum)");

            var indexes = new Dictionary<string, ScannedIndex>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                var tableName = Str(row, "table_name");
                ScannedTable table;
                if (string.IsNullOrEmpty(tableName) || !byTable.TryGetValue(tableName, out table))
                {
                    continue;
                }

                var indexName = Str(row, "index_name") ?? "";
                var key = tableName + "|" + indexName;
                ScannedIndex index;
                if (!indexes.TryGetValue(key, out index))
                {
                    index = new ScannedIndex { Name = indexName, Unique = Truthy(row, "is_unique") };
                    indexes[key] = index;
                    table.Indexes.Add(index);
                }

                var columnName = Str(row, "column_name");
                if (!string.IsNullOrEmpty(columnName))
                {
                    index.Columns.Add(columnName);
                }
            }

            foreach (var table in byTable.Values)
            {
                table.Indexes.RemoveAll(x => x.Columns.Count == 0);
            }
        }

        private void ReadForeignKeys(IDbExecutor executor, Dictionary<string, ScannedTable> byTable)
        {
            var rows = executor.Query(
                "SELECT con.conname AS constraint_name, t.relname AS table_name, a.attname AS column_name, "
                + "ft.relname AS ref_table, fa.attname AS ref_column, con.confdeltype, con.confupdtype "
                + "FROM pg_constraint con "
                + "JOIN pg_class t ON t.oid = con.conrelid "
                + "JOIN pg_namespace n ON n.oid = t.relnamespace "
                + "JOIN pg_class ft ON ft.oid = con.confrelid "
                + "JOIN LATERAL unnest(con.conkey, con.confkey) WITH ORDINALITY AS k(attnum, fattnum, ord) ON true "
                + "JOIN pg_attribute a ON a.attrelid = con.conrelid AND a.attnum = k.attnum "
                + "JOIN pg_attribute fa ON fa.attrelid = con.confrelid AND fa.attnum = k.fattnum "
                + "WHERE con.contype = 'f' AND n.nspname = current_schema() "
                + "ORDER BY t.relname, con.conname, k.ord");

            var keys = new Dictionary<string, ForeignKeyOperation>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                var tableName = Str(row, "table_name");
                ScannedTable table;
                if (string.IsNullOrEmpty(tableName) || !byTable.TryGetValue(tableName, out table))
                {
                    continue;
                }

                var constraintName = Str(row, "constraint_name") ?? "";
                var key = tableName + "|" + constraintName;
                ForeignKeyOperation foreignKey;
                if (!keys.TryGetValue(key, out foreignKey))
                {
                    foreignKey = new ForeignKeyOperation
                    {
                        Table = tableName,
                        Name = constraintName,
                        RefTable = Str(row, "ref_table"),
                        OnDelete = ActionOf(Str(row, "confdeltype")),
                        OnUpdate = ActionOf(Str(row, "confupdtype"))
                    };
                    keys[key] = foreignKey;
                    table.ForeignKeys.Add(foreignKey);
                }

                var column = Str(row, "column_name");
                var refColumn = Str(row, "ref_column");
                if (!string.IsNullOrEmpty(column))
                {
                    foreignKey.Columns.Add(column);
                }
                if (!string.IsNullOrEmpty(refColumn))
                {
                    foreignKey.RefColumns.Add(refColumn);
                }
            }
        }

        private void ReadCheckConstraints(IDbExecutor executor, Dictionary<string, ScannedTable> byTable)
        {
            var rows = executor.Query(
                "SELECT t.relname AS table_name, con.conname AS constraint_name, pg_get_constraintdef(con.oid) AS definition "
                + "FROM pg_constraint con "
                + "JOIN pg_class t ON t.oid = con.conrelid "
                + "JOIN pg_namespace n ON n.oid = t.relnamespace "
                + "WHERE con.contype = 'c' AND n.nspname = current_schema()");

            foreach (var row in rows)
            {
                var tableName = Str(row, "table_name");
                ScannedTable table;
                if (string.IsNullOrEmpty(tableName) || !byTable.TryGetValue(tableName, out table))
                {
                    continue;
                }

                var definition = Str(row, "definition") ?? "";
                if (definition.StartsWith("CHECK", StringComparison.OrdinalIgnoreCase))
                {
                    definition = definition.Substring(5).Trim();
                }

                table.CheckConstraints.Add(new CheckConstraintOperation
                {
                    Table = tableName,
                    Name = Str(row, "constraint_name") ?? "",
                    Expression = MySqlMetadataReader.StripOuterParentheses(definition)
                });
            }
        }

        private static string ActionOf(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return null;
            }
            switch (code.Trim().ToLowerInvariant())
            {
                case "c":
                    return "CASCADE";
                case "n":
                    return "SET NULL";
                case "d":
                    return "SET DEFAULT";
                case "r":
                    return "RESTRICT";
                default:
                    return null;
            }
        }

        private static void MapType(string baseType, ScannedColumn column)
        {
            switch ((baseType ?? "").ToLowerInvariant())
            {
                case "bool":
                    column.Type = LogicalType.Boolean;
                    return;
                case "int2":
                    column.Type = LogicalType.Int16;
                    return;
                case "int4":
                    column.Type = LogicalType.Int32;
                    return;
                case "int8":
                    column.Type = LogicalType.Int64;
                    return;
                case "float4":
                    column.Type = LogicalType.Float32;
                    return;
                case "float8":
                    column.Type = LogicalType.Float64;
                    return;
                case "numeric":
                case "money":
                    column.Type = LogicalType.Decimal;
                    return;
                case "bpchar":
                    column.Type = LogicalType.FixedString;
                    return;
                case "varchar":
                    column.Type = LogicalType.String;
                    return;
                case "text":
                case "name":
                    column.Type = LogicalType.Text;
                    column.Length = null;
                    return;
                case "json":
                case "jsonb":
                    column.Type = LogicalType.Json;
                    return;
                case "xml":
                    column.Type = LogicalType.Xml;
                    return;
                case "date":
                    column.Type = LogicalType.Date;
                    return;
                case "time":
                case "timetz":
                    column.Type = LogicalType.Time;
                    return;
                case "timestamp":
                    column.Type = LogicalType.DateTime;
                    return;
                case "timestamptz":
                    column.Type = LogicalType.DateTimeOffset;
                    return;
                case "bytea":
                    column.Type = column.Length.HasValue ? LogicalType.Binary : LogicalType.Blob;
                    return;
                case "uuid":
                    column.Type = LogicalType.Uuid;
                    return;
            }

            column.Type = LogicalType.Text;
        }

        internal static string NullIfEmpty(string value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}

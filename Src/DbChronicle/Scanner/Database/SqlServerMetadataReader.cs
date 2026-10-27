using System;
using System.Collections.Generic;

using DbChronicle.Common.Enums;
using DbChronicle.Execution;
using DbChronicle.Models;
using DbChronicle.Models.Operations;

namespace DbChronicle.Scanner.Database
{
    /// <summary>SQL Server 元数据读取：sys.tables / sys.columns / sys.indexes / sys.foreign_keys / 扩展属性。</summary>
    public sealed class SqlServerMetadataReader : MetadataReaderBase
    {
        public override string DialectName => "sqlserver";

        public override ScanResult Read(IDbExecutor executor, ScanDatabaseOptions options)
        {
            var result = new ScanResult();
            var byTable = new Dictionary<string, ScannedTable>(StringComparer.OrdinalIgnoreCase);

            var tables = executor.Query(
                "SELECT t.name AS table_name, CAST(ep.value AS NVARCHAR(MAX)) AS comment "
                + "FROM sys.tables t "
                + "LEFT JOIN sys.extended_properties ep ON ep.major_id = t.object_id AND ep.minor_id = 0 AND ep.name = 'MS_Description' "
                + "WHERE t.is_ms_shipped = 0 ORDER BY t.name");

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
                "SELECT t.name AS table_name, c.name AS column_name, ty.name AS base_type, "
                + "c.max_length, c.precision, c.scale, c.is_nullable, c.is_identity, "
                + "dc.definition AS column_default, CAST(ep.value AS NVARCHAR(MAX)) AS comment "
                + "FROM sys.columns c "
                + "JOIN sys.tables t ON t.object_id = c.object_id "
                + "JOIN sys.types ty ON ty.user_type_id = c.user_type_id "
                + "LEFT JOIN sys.default_constraints dc ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id "
                + "LEFT JOIN sys.extended_properties ep ON ep.major_id = c.object_id AND ep.minor_id = c.column_id AND ep.name = 'MS_Description' "
                + "WHERE t.is_ms_shipped = 0 ORDER BY t.name, c.column_id");

            foreach (var row in rows)
            {
                var tableName = Str(row, "table_name");
                ScannedTable table;
                if (string.IsNullOrEmpty(tableName) || !byTable.TryGetValue(tableName, out table))
                {
                    continue;
                }

                var baseType = (Str(row, "base_type") ?? "").ToLowerInvariant();
                var maxLength = Int(row, "max_length");
                var isIdentity = Truthy(row, "is_identity");

                var column = new ScannedColumn
                {
                    Name = Str(row, "column_name") ?? "",
                    Nullable = Truthy(row, "is_nullable"),
                    AutoIncrement = isIdentity,
                    DefaultValue = NormalizeDefault(Str(row, "column_default")),
                    Comment = options.IncludeComments ? NullIfEmpty(Str(row, "comment")) : null,
                    Precision = Int(row, "precision"),
                    Scale = Int(row, "scale")
                };

                // max_length 是字节数；n 前缀类型（nvarchar/nchar）的字符数需除以 2
                if (maxLength.HasValue && maxLength.Value > 0)
                {
                    column.Length = baseType.StartsWith("n", StringComparison.OrdinalIgnoreCase) ? maxLength.Value / 2 : maxLength.Value;
                }
                else if (maxLength.HasValue && maxLength.Value == -1)
                {
                    column.Length = null;
                }

                MapType(baseType, maxLength, column);
                table.Columns.Add(column);
            }
        }

        private void ReadPrimaryKeys(IDbExecutor executor, Dictionary<string, ScannedTable> byTable)
        {
            var rows = executor.Query(
                "SELECT t.name AS table_name, c.name AS column_name "
                + "FROM sys.indexes i "
                + "JOIN sys.tables t ON t.object_id = i.object_id "
                + "JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id "
                + "JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id "
                + "WHERE i.is_primary_key = 1 AND t.is_ms_shipped = 0 "
                + "ORDER BY t.name, ic.key_ordinal");

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
                    }
                }
            }
        }

        private void ReadIndexes(IDbExecutor executor, Dictionary<string, ScannedTable> byTable)
        {
            var rows = executor.Query(
                "SELECT t.name AS table_name, i.name AS index_name, i.is_unique, c.name AS column_name "
                + "FROM sys.indexes i "
                + "JOIN sys.tables t ON t.object_id = i.object_id "
                + "JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0 "
                + "JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id "
                + "WHERE i.is_primary_key = 0 AND i.is_unique_constraint = 0 AND i.name IS NOT NULL AND t.is_ms_shipped = 0 "
                + "ORDER BY t.name, i.name, ic.key_ordinal");

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

            // 唯一约束（UQ）单独还原
            var uqRows = executor.Query(
                "SELECT t.name AS table_name, kc.name AS constraint_name, c.name AS column_name "
                + "FROM sys.key_constraints kc "
                + "JOIN sys.tables t ON t.object_id = kc.parent_object_id "
                + "JOIN sys.index_columns ic ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id "
                + "JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id "
                + "WHERE kc.type = 'UQ' AND t.is_ms_shipped = 0 ORDER BY t.name, kc.name, ic.key_ordinal");

            var uniques = new Dictionary<string, UniqueConstraintOperation>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in uqRows)
            {
                var tableName = Str(row, "table_name");
                ScannedTable table;
                if (string.IsNullOrEmpty(tableName) || !byTable.TryGetValue(tableName, out table))
                {
                    continue;
                }

                var constraintName = Str(row, "constraint_name") ?? "";
                var key = tableName + "|" + constraintName;
                UniqueConstraintOperation unique;
                if (!uniques.TryGetValue(key, out unique))
                {
                    unique = new UniqueConstraintOperation { Table = tableName, Name = constraintName };
                    uniques[key] = unique;
                    table.UniqueConstraints.Add(unique);
                }

                var columnName = Str(row, "column_name");
                if (!string.IsNullOrEmpty(columnName))
                {
                    unique.Columns.Add(columnName);
                }
            }
        }

        private void ReadForeignKeys(IDbExecutor executor, Dictionary<string, ScannedTable> byTable)
        {
            var rows = executor.Query(
                "SELECT fk.name AS constraint_name, t.name AS table_name, c.name AS column_name, "
                + "rt.name AS ref_table, rc.name AS ref_column, "
                + "fk.delete_referential_action_desc AS on_delete, fk.update_referential_action_desc AS on_update "
                + "FROM sys.foreign_keys fk "
                + "JOIN sys.tables t ON t.object_id = fk.parent_object_id "
                + "JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id "
                + "JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id "
                + "JOIN sys.tables rt ON rt.object_id = fkc.referenced_object_id "
                + "JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id "
                + "ORDER BY t.name, fk.name, fkc.constraint_column_id");

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
                        OnDelete = Str(row, "on_delete"),
                        OnUpdate = Str(row, "on_update")
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
                "SELECT t.name AS table_name, cc.name AS constraint_name, cc.definition "
                + "FROM sys.check_constraints cc "
                + "JOIN sys.tables t ON t.object_id = cc.parent_object_id "
                + "WHERE t.is_ms_shipped = 0");

            foreach (var row in rows)
            {
                var tableName = Str(row, "table_name");
                ScannedTable table;
                if (string.IsNullOrEmpty(tableName) || !byTable.TryGetValue(tableName, out table))
                {
                    continue;
                }

                table.CheckConstraints.Add(new CheckConstraintOperation
                {
                    Table = tableName,
                    Name = Str(row, "constraint_name") ?? "",
                    Expression = MySqlMetadataReader.StripOuterParentheses(Str(row, "definition") ?? "")
                });
            }
        }

        private static void MapType(string baseType, int? maxLength, ScannedColumn column)
        {
            switch (baseType)
            {
                case "bit":
                    column.Type = LogicalType.Boolean;
                    return;
                case "tinyint":
                    column.Type = LogicalType.Int8;
                    return;
                case "smallint":
                    column.Type = LogicalType.Int16;
                    return;
                case "int":
                    column.Type = LogicalType.Int32;
                    return;
                case "bigint":
                    column.Type = LogicalType.Int64;
                    return;
                case "real":
                    column.Type = LogicalType.Float32;
                    return;
                case "float":
                    column.Type = LogicalType.Float64;
                    return;
                case "decimal":
                case "numeric":
                case "money":
                case "smallmoney":
                    column.Type = LogicalType.Decimal;
                    return;
                case "nchar":
                case "char":
                    column.Type = LogicalType.FixedString;
                    return;
                case "nvarchar":
                case "varchar":
                    column.Type = maxLength.HasValue && maxLength.Value == -1 ? LogicalType.Text : LogicalType.String;
                    if (column.Type == LogicalType.Text)
                    {
                        column.Length = null;
                    }
                    return;
                case "ntext":
                case "text":
                    column.Type = LogicalType.Text;
                    column.Length = null;
                    return;
                case "xml":
                    column.Type = LogicalType.Xml;
                    return;
                case "date":
                    column.Type = LogicalType.Date;
                    return;
                case "time":
                    column.Type = LogicalType.Time;
                    return;
                case "datetime":
                case "datetime2":
                case "smalldatetime":
                    column.Type = LogicalType.DateTime;
                    return;
                case "datetimeoffset":
                    column.Type = LogicalType.DateTimeOffset;
                    return;
                case "binary":
                case "varbinary":
                    column.Type = maxLength.HasValue && maxLength.Value == -1 ? LogicalType.Blob : LogicalType.Binary;
                    if (column.Type == LogicalType.Blob)
                    {
                        column.Length = null;
                    }
                    return;
                case "image":
                    column.Type = LogicalType.Blob;
                    column.Length = null;
                    return;
                case "uniqueidentifier":
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

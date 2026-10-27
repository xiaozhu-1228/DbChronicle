using System;
using System.Collections.Generic;

using DbChronicle.Common.Enums;
using DbChronicle.Execution;
using DbChronicle.Models;
using DbChronicle.Models.Operations;

namespace DbChronicle.Scanner.Database
{
    /// <summary>MySQL / MariaDB 元数据读取：information_schema（表 / 列 / 索引 / 外键 / CHECK）。</summary>
    public class MySqlMetadataReader : MetadataReaderBase
    {
        public override string DialectName => "mysql";

        public override ScanResult Read(IDbExecutor executor, ScanDatabaseOptions options)
        {
            var result = new ScanResult();
            var tables = executor.Query(
                "SELECT TABLE_NAME, TABLE_COMMENT FROM information_schema.TABLES "
                + "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME");

            var byTable = new Dictionary<string, ScannedTable>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in tables)
            {
                var name = Str(row, "TABLE_NAME");
                if (string.IsNullOrEmpty(name) || !options.ShouldInclude(name))
                {
                    continue;
                }

                var table = new ScannedTable
                {
                    Name = name,
                    Comment = options.IncludeComments ? NullIfEmpty(Str(row, "TABLE_COMMENT")) : null
                };
                byTable[name] = table;
                result.Tables.Add(table);
            }

            if (byTable.Count == 0)
            {
                return result;
            }

            ReadColumns(executor, byTable, options);
            if (options.IncludeIndexes)
            {
                ReadIndexes(executor, byTable, result);
            }
            if (options.IncludeForeignKeys)
            {
                ReadForeignKeys(executor, byTable);
            }
            if (options.IncludeCheckConstraints)
            {
                ReadCheckConstraints(executor, byTable, result);
            }

            return result;
        }

        private void ReadColumns(IDbExecutor executor, Dictionary<string, ScannedTable> byTable, ScanDatabaseOptions options)
        {
            var rows = executor.Query(
                "SELECT TABLE_NAME, COLUMN_NAME, COLUMN_TYPE, DATA_TYPE, IS_NULLABLE, COLUMN_DEFAULT, EXTRA, "
                + "COLUMN_COMMENT, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE "
                + "FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() "
                + "ORDER BY TABLE_NAME, ORDINAL_POSITION");

            foreach (var row in rows)
            {
                var tableName = Str(row, "TABLE_NAME");
                ScannedTable table;
                if (string.IsNullOrEmpty(tableName) || !byTable.TryGetValue(tableName, out table))
                {
                    continue;
                }

                var columnType = Str(row, "COLUMN_TYPE") ?? Str(row, "DATA_TYPE") ?? "";
                var physical = PhysicalType.Parse(columnType);
                var extra = Str(row, "EXTRA") ?? "";
                var isPrimaryKey = false;

                var column = new ScannedColumn
                {
                    Name = Str(row, "COLUMN_NAME") ?? "",
                    Nullable = string.Equals(Str(row, "IS_NULLABLE"), "YES", StringComparison.OrdinalIgnoreCase),
                    AutoIncrement = extra.IndexOf("auto_increment", StringComparison.OrdinalIgnoreCase) >= 0,
                    Unsigned = columnType.IndexOf("unsigned", StringComparison.OrdinalIgnoreCase) >= 0,
                    DefaultValue = NormalizeDefault(Str(row, "COLUMN_DEFAULT")),
                    Comment = options.IncludeComments ? NullIfEmpty(Str(row, "COLUMN_COMMENT")) : null,
                    PrimaryKey = isPrimaryKey
                };

                column.Length = Int(row, "CHARACTER_MAXIMUM_LENGTH");
                column.Precision = Int(row, "NUMERIC_PRECISION");
                column.Scale = Int(row, "NUMERIC_SCALE");

                MapType(physical, columnType, column);
                table.Columns.Add(column);
            }
        }

        private void ReadIndexes(IDbExecutor executor, Dictionary<string, ScannedTable> byTable, ScanResult result)
        {
            var rows = executor.Query(
                "SELECT TABLE_NAME, INDEX_NAME, NON_UNIQUE, SEQ_IN_INDEX, COLUMN_NAME "
                + "FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() "
                + "ORDER BY TABLE_NAME, INDEX_NAME, SEQ_IN_INDEX");

            var indexes = new Dictionary<string, ScannedIndex>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                var tableName = Str(row, "TABLE_NAME");
                ScannedTable table;
                if (string.IsNullOrEmpty(tableName) || !byTable.TryGetValue(tableName, out table))
                {
                    continue;
                }

                var indexName = Str(row, "INDEX_NAME") ?? "";
                var columnName = Str(row, "COLUMN_NAME");
                if (string.Equals(indexName, "PRIMARY", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(columnName) && !table.PrimaryKeyColumns.Contains(columnName))
                    {
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
                    continue;
                }

                var key = tableName + "|" + indexName;
                ScannedIndex index;
                if (!indexes.TryGetValue(key, out index))
                {
                    index = new ScannedIndex
                    {
                        Name = indexName,
                        Unique = (Int(row, "NON_UNIQUE") ?? 1) == 0
                    };
                    indexes[key] = index;
                    table.Indexes.Add(index);
                }

                if (!string.IsNullOrEmpty(columnName))
                {
                    index.Columns.Add(columnName);
                }
                else
                {
                    result.Warnings.Add("表 " + tableName + " 的索引 " + indexName + " 含表达式列，已跳过列定义");
                }
            }
        }

        private void ReadForeignKeys(IDbExecutor executor, Dictionary<string, ScannedTable> byTable)
        {
            var rows = executor.Query(
                "SELECT k.CONSTRAINT_NAME, k.TABLE_NAME, k.COLUMN_NAME, k.REFERENCED_TABLE_NAME, "
                + "k.REFERENCED_COLUMN_NAME, k.ORDINAL_POSITION, r.UPDATE_RULE, r.DELETE_RULE "
                + "FROM information_schema.KEY_COLUMN_USAGE k "
                + "JOIN information_schema.REFERENTIAL_CONSTRAINTS r "
                + "ON r.CONSTRAINT_SCHEMA = k.CONSTRAINT_SCHEMA AND r.CONSTRAINT_NAME = k.CONSTRAINT_NAME "
                + "WHERE k.TABLE_SCHEMA = DATABASE() AND k.REFERENCED_TABLE_NAME IS NOT NULL "
                + "ORDER BY k.TABLE_NAME, k.CONSTRAINT_NAME, k.ORDINAL_POSITION");

            var keys = new Dictionary<string, ForeignKeyOperation>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                var tableName = Str(row, "TABLE_NAME");
                ScannedTable table;
                if (string.IsNullOrEmpty(tableName) || !byTable.TryGetValue(tableName, out table))
                {
                    continue;
                }

                var constraintName = Str(row, "CONSTRAINT_NAME") ?? "";
                var key = tableName + "|" + constraintName;
                ForeignKeyOperation foreignKey;
                if (!keys.TryGetValue(key, out foreignKey))
                {
                    foreignKey = new ForeignKeyOperation
                    {
                        Table = tableName,
                        Name = constraintName,
                        RefTable = Str(row, "REFERENCED_TABLE_NAME"),
                        OnDelete = NormalizeAction(Str(row, "DELETE_RULE")),
                        OnUpdate = NormalizeAction(Str(row, "UPDATE_RULE"))
                    };
                    keys[key] = foreignKey;
                    table.ForeignKeys.Add(foreignKey);
                }

                var column = Str(row, "COLUMN_NAME");
                var refColumn = Str(row, "REFERENCED_COLUMN_NAME");
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

        private void ReadCheckConstraints(IDbExecutor executor, Dictionary<string, ScannedTable> byTable, ScanResult result)
        {
            List<Dictionary<string, object>> rows;
            try
            {
                rows = executor.Query(
                    "SELECT tc.TABLE_NAME, cc.CONSTRAINT_NAME, cc.CHECK_CLAUSE "
                    + "FROM information_schema.TABLE_CONSTRAINTS tc "
                    + "JOIN information_schema.CHECK_CONSTRAINTS cc "
                    + "ON cc.CONSTRAINT_SCHEMA = tc.CONSTRAINT_SCHEMA AND cc.CONSTRAINT_NAME = tc.CONSTRAINT_NAME "
                    + "WHERE tc.TABLE_SCHEMA = DATABASE() AND tc.CONSTRAINT_TYPE = 'CHECK'");
            }
            catch (Exception ex)
            {
                result.Warnings.Add("当前版本不支持 CHECK 约束元数据（" + ex.Message + "），已跳过");
                return;
            }

            foreach (var row in rows)
            {
                var tableName = Str(row, "TABLE_NAME");
                ScannedTable table;
                if (string.IsNullOrEmpty(tableName) || !byTable.TryGetValue(tableName, out table))
                {
                    continue;
                }

                table.CheckConstraints.Add(new CheckConstraintOperation
                {
                    Table = tableName,
                    Name = Str(row, "CONSTRAINT_NAME") ?? "",
                    Expression = StripOuterParentheses(Str(row, "CHECK_CLAUSE") ?? "")
                });
            }
        }

        internal static void MapType(PhysicalType physical, string columnType, ScannedColumn column)
        {
            var baseName = physical.BaseName;

            if (baseName == "tinyint")
            {
                // MySQL 的 BOOLEAN 就是 tinyint(1)
                if (!column.AutoIncrement && columnType.IndexOf("tinyint(1)", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    column.Type = LogicalType.Boolean;
                    return;
                }
                column.Type = LogicalType.Int8;
                return;
            }
            if (baseName == "smallint")
            {
                column.Type = LogicalType.Int16;
                return;
            }
            if (baseName == "mediumint" || baseName == "int" || baseName == "integer")
            {
                column.Type = LogicalType.Int32;
                return;
            }
            if (baseName == "bigint")
            {
                column.Type = LogicalType.Int64;
                return;
            }
            if (baseName == "float")
            {
                column.Type = LogicalType.Float32;
                return;
            }
            if (baseName == "double" || baseName == "real")
            {
                column.Type = LogicalType.Float64;
                return;
            }
            if (baseName == "decimal" || baseName == "numeric")
            {
                column.Type = LogicalType.Decimal;
                return;
            }
            if (baseName == "char")
            {
                column.Type = LogicalType.FixedString;
                return;
            }
            if (baseName == "varchar")
            {
                column.Type = LogicalType.String;
                return;
            }
            if (baseName.EndsWith("text", StringComparison.OrdinalIgnoreCase))
            {
                column.Type = LogicalType.Text;
                column.Length = null;
                return;
            }
            if (baseName == "json")
            {
                column.Type = LogicalType.Json;
                return;
            }
            if (baseName == "date")
            {
                column.Type = LogicalType.Date;
                return;
            }
            if (baseName == "time")
            {
                column.Type = LogicalType.Time;
                return;
            }
            if (baseName == "datetime" || baseName == "timestamp")
            {
                column.Type = LogicalType.DateTime;
                return;
            }
            if (baseName == "year")
            {
                column.Type = LogicalType.Int16;
                return;
            }
            if (baseName == "binary" || baseName == "varbinary")
            {
                column.Type = LogicalType.Binary;
                return;
            }
            if (baseName.EndsWith("blob", StringComparison.OrdinalIgnoreCase))
            {
                column.Type = LogicalType.Blob;
                column.Length = null;
                return;
            }
            if (baseName == "enum" || baseName == "set")
            {
                column.Type = LogicalType.String;
                return;
            }

            column.Type = LogicalType.Text;
        }

        internal static string StripOuterParentheses(string value)
        {
            var text = (value ?? "").Trim();
            while (text.Length >= 2 && text[0] == '(' && text[text.Length - 1] == ')')
            {
                text = text.Substring(1, text.Length - 2).Trim();
            }
            return text;
        }

        internal static string NullIfEmpty(string value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}

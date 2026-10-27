using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using DbChronicle.Common.Enums;
using DbChronicle.Execution;
using DbChronicle.Models;
using DbChronicle.Models.Operations;

namespace DbChronicle.Scanner.Database
{
    /// <summary>SQLite 元数据读取：sqlite_master + PRAGMA（table_info / index_list / foreign_key_list）。</summary>
    public sealed class SqliteMetadataReader : MetadataReaderBase
    {
        private static readonly Regex AutoIncrementPattern = new Regex(@"\bAUTOINCREMENT\b", RegexOptions.IgnoreCase);

        public override string DialectName => "sqlite";

        public override ScanResult Read(IDbExecutor executor, ScanDatabaseOptions options)
        {
            var result = new ScanResult();
            var tables = executor.Query(
                "SELECT name, sql FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name");

            foreach (var row in tables)
            {
                var name = Str(row, "name");
                if (string.IsNullOrEmpty(name) || !options.ShouldInclude(name))
                {
                    continue;
                }

                var table = new ScannedTable { Name = name };
                var createSql = Str(row, "sql") ?? "";
                ReadColumns(executor, table, createSql, result);

                if (options.IncludeIndexes)
                {
                    ReadIndexes(executor, table);
                }
                if (options.IncludeForeignKeys)
                {
                    ReadForeignKeys(executor, table);
                }
                if (options.IncludeCheckConstraints)
                {
                    ReadCheckConstraints(table, createSql);
                }

                if (table.Columns.Count > 0)
                {
                    result.Tables.Add(table);
                }
            }

            return result;
        }

        private void ReadColumns(IDbExecutor executor, ScannedTable table, string createSql, ScanResult result)
        {
            var rows = executor.Query("PRAGMA table_info(" + Quote(table.Name) + ")");
            var autoIncrement = AutoIncrementPattern.IsMatch(createSql);

            foreach (var row in rows)
            {
                var name = Str(row, "name");
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var declared = Str(row, "type") ?? "";
                var physical = PhysicalType.Parse(declared);
                var notNull = Truthy(row, "notnull");
                var primaryKeyOrder = Int(row, "pk") ?? 0;
                var isPrimaryKey = primaryKeyOrder > 0;

                var column = new ScannedColumn
                {
                    Name = name,
                    // SQLite 的 INTEGER PRIMARY KEY 允许省略 NOT NULL，但语义上不可空
                    Nullable = !notNull && !isPrimaryKey,
                    PrimaryKey = isPrimaryKey,
                    AutoIncrement = autoIncrement && isPrimaryKey && physical.Contains("int"),
                    DefaultValue = NormalizeDefault(Str(row, "dflt_value"))
                };

                MapType(physical, column);
                table.Columns.Add(column);
                if (isPrimaryKey)
                {
                    table.PrimaryKeyColumns.Add(name);
                }
            }
        }

        private void ReadIndexes(IDbExecutor executor, ScannedTable table)
        {
            var rows = executor.Query("PRAGMA index_list(" + Quote(table.Name) + ")");
            foreach (var row in rows)
            {
                var indexName = Str(row, "name");
                if (string.IsNullOrEmpty(indexName))
                {
                    continue;
                }

                var origin = Str(row, "origin") ?? "";
                if (string.Equals(origin, "pk", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var unique = Truthy(row, "unique");
                var columns = ReadIndexColumns(executor, indexName);
                if (columns.Count == 0)
                {
                    continue;
                }

                if (string.Equals(origin, "u", StringComparison.OrdinalIgnoreCase))
                {
                    // 列级 UNIQUE 约束自动生成的索引：还原为唯一约束
                    var uniqueConstraint = new UniqueConstraintOperation { Table = table.Name, Name = indexName };
                    uniqueConstraint.Columns.AddRange(columns);
                    table.UniqueConstraints.Add(uniqueConstraint);
                    continue;
                }

                var index = new ScannedIndex { Name = indexName, Unique = unique };
                index.Columns.AddRange(columns);
                table.Indexes.Add(index);
            }
        }

        private static List<string> ReadIndexColumns(IDbExecutor executor, string indexName)
        {
            var columns = new List<string>();
            foreach (var row in executor.Query("PRAGMA index_info(" + Quote(indexName) + ")"))
            {
                var name = Str(row, "name");
                if (!string.IsNullOrEmpty(name))
                {
                    columns.Add(name);
                }
            }
            return columns;
        }

        private void ReadForeignKeys(IDbExecutor executor, ScannedTable table)
        {
            var rows = executor.Query("PRAGMA foreign_key_list(" + Quote(table.Name) + ")");
            var byId = new Dictionary<string, ForeignKeyOperation>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                var id = Str(row, "id") ?? "0";
                ForeignKeyOperation foreignKey;
                if (!byId.TryGetValue(id, out foreignKey))
                {
                    foreignKey = new ForeignKeyOperation
                    {
                        Table = table.Name,
                        Name = "fk_" + table.Name + "_" + id,
                        RefTable = Str(row, "table"),
                        OnDelete = NormalizeAction(Str(row, "on_delete")),
                        OnUpdate = NormalizeAction(Str(row, "on_update"))
                    };
                    byId[id] = foreignKey;
                    table.ForeignKeys.Add(foreignKey);
                }

                var from = Str(row, "from");
                var to = Str(row, "to");
                if (!string.IsNullOrEmpty(from))
                {
                    foreignKey.Columns.Add(from);
                }
                if (!string.IsNullOrEmpty(to))
                {
                    foreignKey.RefColumns.Add(to);
                }
            }

            foreach (var foreignKey in table.ForeignKeys)
            {
                foreignKey.Name = "fk_" + table.Name + "_" + string.Join("_", foreignKey.Columns);
            }
        }

        /// <summary>SQLite 无 CHECK 元数据表，只能从建表语句里解析 CONSTRAINT name CHECK (expr)。</summary>
        private void ReadCheckConstraints(ScannedTable table, string createSql)
        {
            if (string.IsNullOrEmpty(createSql))
            {
                return;
            }

            var index = 0;
            var ordinal = 0;
            while (true)
            {
                var checkAt = createSql.IndexOf("CHECK", index, StringComparison.OrdinalIgnoreCase);
                if (checkAt < 0)
                {
                    break;
                }

                var open = createSql.IndexOf('(', checkAt + 5);
                if (open < 0)
                {
                    break;
                }

                var close = FindMatchingParenthesis(createSql, open);
                if (close < 0)
                {
                    break;
                }

                var expression = createSql.Substring(open + 1, close - open - 1).Trim();
                var name = FindConstraintName(createSql, checkAt);
                if (string.IsNullOrEmpty(name))
                {
                    ordinal++;
                    name = "ck_" + table.Name + "_" + ordinal;
                }

                table.CheckConstraints.Add(new CheckConstraintOperation
                {
                    Table = table.Name,
                    Name = name,
                    Expression = expression
                });

                index = close + 1;
            }
        }

        private static string FindConstraintName(string sql, int checkAt)
        {
            // 形如 CONSTRAINT "ck_x" CHECK (...)：从 CHECK 往前找最近的 CONSTRAINT
            var constraintAt = sql.LastIndexOf("CONSTRAINT", checkAt, StringComparison.OrdinalIgnoreCase);
            if (constraintAt < 0)
            {
                return null;
            }

            var segment = sql.Substring(constraintAt + "CONSTRAINT".Length, checkAt - constraintAt - "CONSTRAINT".Length).Trim();
            if (segment.Length == 0)
            {
                return null;
            }

            var quote = segment[0];
            if (quote == '"' || quote == '`' || quote == '[')
            {
                var closing = quote == '[' ? ']' : quote;
                var end = segment.IndexOf(closing, 1);
                return end > 1 ? segment.Substring(1, end - 1) : null;
            }

            var space = segment.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
            return space > 0 ? segment.Substring(0, space) : segment;
        }

        private static int FindMatchingParenthesis(string sql, int openIndex)
        {
            var depth = 0;
            var inString = false;
            for (var i = openIndex; i < sql.Length; i++)
            {
                var ch = sql[i];
                if (ch == '\'')
                {
                    inString = !inString;
                    continue;
                }
                if (inString)
                {
                    continue;
                }
                if (ch == '(')
                {
                    depth++;
                }
                else if (ch == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        private static void MapType(PhysicalType physical, ScannedColumn column)
        {
            if (physical.Contains("int"))
            {
                column.Type = LogicalType.Int64;
                return;
            }
            if (physical.IsAny("boolean", "bool"))
            {
                column.Type = LogicalType.Boolean;
                return;
            }
            if (physical.Contains("char") || physical.Contains("clob"))
            {
                column.Type = physical.First.HasValue ? LogicalType.String : LogicalType.Text;
                column.Length = physical.First;
                return;
            }
            if (physical.Contains("text"))
            {
                column.Type = LogicalType.Text;
                return;
            }
            if (physical.Contains("blob") || physical.BaseName.Length == 0)
            {
                column.Type = LogicalType.Blob;
                return;
            }
            if (physical.Contains("real") || physical.Contains("floa") || physical.Contains("doub"))
            {
                column.Type = LogicalType.Float64;
                return;
            }
            if (physical.Contains("dec") || physical.Contains("num"))
            {
                column.Type = LogicalType.Decimal;
                column.Precision = physical.First;
                column.Scale = physical.Second;
                return;
            }
            if (physical.Is("date"))
            {
                column.Type = LogicalType.Date;
                return;
            }
            if (physical.Contains("datetime") || physical.Contains("timestamp"))
            {
                column.Type = LogicalType.DateTime;
                return;
            }
            if (physical.Is("time"))
            {
                column.Type = LogicalType.Time;
                return;
            }
            if (physical.Contains("json"))
            {
                column.Type = LogicalType.Json;
                return;
            }
            if (physical.Contains("uuid"))
            {
                column.Type = LogicalType.Uuid;
                return;
            }

            column.Type = LogicalType.Text;
        }

        private static string Quote(string identifier)
        {
            return "\"" + (identifier ?? "").Replace("\"", "\"\"") + "\"";
        }
    }
}

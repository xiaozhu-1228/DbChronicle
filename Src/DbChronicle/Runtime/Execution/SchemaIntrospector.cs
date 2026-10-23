using System;
using System.Collections.Generic;
using DbChronicle.Abstractions.Diagnostics;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Splitting;
using DbChronicle.Domain.Sql;
using DbChronicle.Domain.Structures;
using DbChronicle.Abstractions.Versioning;
using DbChronicle.Runtime.Versioning;

using DbChronicle.Abstractions.Execution;
using DbChronicle.Compilation.Dialects;
using DbChronicle.Configuration;
using DbChronicle.Domain.Model;
using DbChronicle.Domain.Operations;
using DbChronicle.Runtime.Execution;
using DbChronicle.Runtime.Rollback;
using DbChronicle.Runtime.Naming;
namespace DbChronicle.Runtime.Execution
{
    public sealed class DriftReport
    {
        public List<string> MissingColumns { get; } = new List<string>();

        public List<string> ExtraColumns { get; } = new List<string>();

        public List<string> UnreadableTables { get; } = new List<string>();

        public bool HasDrift
        {
            get { return MissingColumns.Count > 0; }
        }
    }

    public sealed class SchemaIntrospector
    {
        public static DriftReport Compare(IDbExecutor executor, StructureModel model)
        {
            var report = new DriftReport();
            var actual = LoadColumns(executor, report);

            foreach (var table in model.Tables.Values)
            {
                HashSet<string> columns;
                if (!actual.TryGetValue(table.Name, out columns))
                {
                    report.UnreadableTables.Add(table.Name);
                    continue;
                }

                foreach (var column in table.Columns.Values)
                {
                    if (!columns.Contains(column.Name))
                    {
                        report.MissingColumns.Add(table.Name + "." + column.Name);
                    }
                }

                foreach (var column in columns)
                {
                    if (!table.Columns.ContainsKey(column))
                    {
                        report.ExtraColumns.Add(table.Name + "." + column);
                    }
                }
            }

            return report;
        }

        /// <summary>列出库中全部业务表（用于通配符 table="*" 的覆盖度提示）。</summary>
        public static List<string> ListTables(IDbExecutor executor)
        {
            var dialect = (executor.DialectName ?? "").ToLowerInvariant();
            string sql;
            switch (dialect)
            {
                case "sqlite":
                    sql = "SELECT name FROM sqlite_master WHERE type='table'";
                    break;

                case "postgresql":
                    sql = "SELECT table_name FROM information_schema.tables"
                        + " WHERE table_schema = 'public' AND table_type = 'BASE TABLE'";
                    break;

                case "sqlserver":
                    sql = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'";
                    break;

                default:
                    sql = "SELECT TABLE_NAME FROM information_schema.TABLES"
                        + " WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE'";
                    break;
            }

            var result = new List<string>();
            foreach (var row in executor.Query(sql))
            {
                foreach (var value in row.Values)
                {
                    if (value == null || value is DBNull)
                    {
                        continue;
                    }

                    var name = Convert.ToString(value);
                    if (!string.IsNullOrEmpty(name)
                        && !name.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase)
                        && !result.Contains(name))
                    {
                        result.Add(name);
                    }
                    break;
                }
            }
            return result;
        }

        private static Dictionary<string, HashSet<string>> LoadColumns(IDbExecutor executor, DriftReport report)
        {
            var dialect = (executor.DialectName ?? "").ToLowerInvariant();
            var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (dialect == "sqlite")
                {
                    LoadSqlite(executor, result);
                    return result;
                }

                string sql;
                switch (dialect)
                {
                    case "mysql":
                    case "mariadb":
                        sql = "SELECT TABLE_NAME AS t, COLUMN_NAME AS c FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE()";
                        break;

                    case "postgresql":
                        sql = "SELECT table_name AS t, column_name AS c FROM information_schema.columns WHERE table_schema = 'public'";
                        break;

                    case "sqlserver":
                        sql = "SELECT TABLE_NAME AS t, COLUMN_NAME AS c FROM INFORMATION_SCHEMA.COLUMNS";
                        break;

                    default:
                        return result;
                }

                foreach (var row in executor.Query(sql))
                {
                    Add(result, Text(row, "t"), Text(row, "c"));
                }
            }
            catch (Exception ex)
            {
                report.UnreadableTables.Add("（读取结构失败：" + ex.Message + "）");
            }

            return result;
        }

        private static void LoadSqlite(IDbExecutor executor, Dictionary<string, HashSet<string>> result)
        {
            var tables = executor.Query("SELECT name FROM sqlite_master WHERE type='table'");
            foreach (var row in tables)
            {
                var table = Text(row, "name");
                if (string.IsNullOrEmpty(table) || table.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var column in executor.Query("PRAGMA table_info('" + table.Replace("'", "''") + "')"))
                {
                    Add(result, table, Text(column, "name"));
                }
            }
        }

        private static void Add(Dictionary<string, HashSet<string>> target, string table, string column)
        {
            if (string.IsNullOrEmpty(table) || string.IsNullOrEmpty(column))
            {
                return;
            }

            HashSet<string> columns;
            if (!target.TryGetValue(table, out columns))
            {
                columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                target[table] = columns;
            }
            columns.Add(column);
        }

        private static string Text(Dictionary<string, object> row, string key)
        {
            object value;
            return row.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : "";
        }
    }
}

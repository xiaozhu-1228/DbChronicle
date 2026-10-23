using System;
using System.Collections.Generic;
using DbChronicle.Abstractions.Execution;
using DbChronicle.Runtime.Execution;

namespace DbChronicle.Demo.MySql
{
    /// <summary>
    /// 读取 MySQL 现有表结构。仅 Demo 使用；产品内的结构化反向工程属于三期 Scanner.Database。
    /// </summary>
    internal sealed class MySqlTableIntrospector
    {
        private readonly IDbExecutor _executor;

        public MySqlTableIntrospector(IDbExecutor executor)
        {
            if (executor == null)
            {
                throw new ArgumentNullException("executor");
            }
            _executor = executor;
        }

        public IReadOnlyList<ColumnSnapshot> Inspect(string table)
        {
            var sql =
                "SELECT COLUMN_NAME, COLUMN_TYPE, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE,"
                + " IS_NULLABLE, COLUMN_DEFAULT, COLUMN_COMMENT, COLUMN_KEY, EXTRA, CHARACTER_SET_NAME, COLLATION_NAME"
                + " FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE()"
                + " AND TABLE_NAME = '" + table.Replace("'", "''") + "' ORDER BY ORDINAL_POSITION";

            var result = new List<ColumnSnapshot>();
            foreach (var row in _executor.Query(sql))
            {
                result.Add(new ColumnSnapshot
                {
                    Name = Text(row, "COLUMN_NAME"),
                    ColumnType = Text(row, "COLUMN_TYPE"),
                    DataType = Text(row, "DATA_TYPE"),
                    Length = Integer(row, "CHARACTER_MAXIMUM_LENGTH"),
                    Precision = Integer(row, "NUMERIC_PRECISION"),
                    Scale = Integer(row, "NUMERIC_SCALE"),
                    Nullable = Text(row, "IS_NULLABLE") == "YES",
                    Default = Text(row, "COLUMN_DEFAULT"),
                    HasDefault = Value(row, "COLUMN_DEFAULT") != null,
                    Comment = Text(row, "COLUMN_COMMENT"),
                    PrimaryKey = Text(row, "COLUMN_KEY") == "PRI",
                    AutoIncrement = Text(row, "EXTRA").IndexOf("auto_increment", StringComparison.OrdinalIgnoreCase) >= 0,
                    Charset = Text(row, "CHARACTER_SET_NAME"),
                    Collate = Text(row, "COLLATION_NAME")
                });
            }
            return result;
        }

        private static string Text(Dictionary<string, object> row, string key)
        {
            var value = Value(row, key);
            return value == null ? "" : Convert.ToString(value);
        }

        private static int? Integer(Dictionary<string, object> row, string key)
        {
            var value = Value(row, key);
            if (value == null)
            {
                return null;
            }

            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
                return null;
            }
        }

        private static object Value(Dictionary<string, object> row, string key)
        {
            object value;
            return row.TryGetValue(key, out value) && value != DBNull.Value ? value : null;
        }
    }
}

using System.Collections.Generic;
using System.Globalization;

using DbChronicle.Models;

namespace DbChronicle.Scanner.Database
{
    /// <summary>元数据读取器的公共取值与列构造函数。</summary>
    public abstract class MetadataReaderBase : IDatabaseMetadataReader
    {
        public abstract string DialectName { get; }

        public abstract ScanResult Read(DbChronicle.Execution.IDbExecutor executor, ScanDatabaseOptions options);

        protected static string Str(IDictionary<string, object> row, params string[] keys)
        {
            foreach (var key in keys)
            {
                foreach (var pair in row)
                {
                    if (!string.Equals(pair.Key, key, System.StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (pair.Value == null || pair.Value is System.DBNull)
                    {
                        return null;
                    }
                    return System.Convert.ToString(pair.Value, CultureInfo.InvariantCulture);
                }
            }
            return null;
        }

        protected static int? Int(IDictionary<string, object> row, params string[] keys)
        {
            var text = Str(row, keys);
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }
            int value;
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : (int?)null;
        }

        protected static long? Long(IDictionary<string, object> row, params string[] keys)
        {
            var text = Str(row, keys);
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }
            long value;
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : (long?)null;
        }

        protected static bool Truthy(IDictionary<string, object> row, params string[] keys)
        {
            var text = Str(row, keys);
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            return text == "1"
                || string.Equals(text, "true", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "yes", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "y", System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>统一把数据库给的默认值整理成逻辑模型可用的字面量（去括号、去多余引号）。</summary>
        protected static string NormalizeDefault(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return null;
            }

            var text = raw.Trim();
            if (text.Length == 0)
            {
                return null;
            }

            if (string.Equals(text, "NULL", System.StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // SQL 里常见 ((0)) / ('x') 这类多余括号
            while (text.Length >= 2 && text[0] == '(' && text[text.Length - 1] == ')')
            {
                text = text.Substring(1, text.Length - 2).Trim();
            }

            return text.Length == 0 ? null : text;
        }

        /// <summary>SQLite / MySQL 的 DDL 里可能出现的转换后缀，统一转成逻辑外键动作。</summary>
        protected static string NormalizeAction(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return null;
            }

            var text = raw.Trim().ToUpperInvariant();
            if (text == "NO ACTION" || text == "RESTRICT")
            {
                return text == "RESTRICT" ? "RESTRICT" : null;
            }
            return text;
        }
    }
}

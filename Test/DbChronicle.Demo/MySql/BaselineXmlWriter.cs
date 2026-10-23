using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DbChronicle.Configuration;
using DbChronicle.Domain.Operations;

namespace DbChronicle.Demo.MySql
{
    /// <summary>
    /// 把表结构快照写成逻辑模型基线 XML（CREATE TABLE + ADD COLUMN）。
    /// </summary>
    internal sealed class BaselineXmlWriter
    {
        public const string BaselineFileName = "00000000_000000_baseline.xml";

        public string Write(DbChronicleConfig config, string table, IReadOnlyList<ColumnSnapshot> columns)
        {
            var directory = config.ResolveMigrationsPath();
            Directory.CreateDirectory(directory);

            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
            sb.Append("<changeUnit id=\"00000000_000000_baseline\" author=\"scanner\" release=\"00000000\">\n");
            sb.Append("  <description>基线：从 MySQL 现有结构生成（").Append(table).Append("）</description>\n");
            sb.Append("  <table name=\"").Append(table).Append("\" action=\"CREATE\">\n");

            if (columns.Count == 0)
            {
                AppendFallbackColumns(sb);
            }
            else
            {
                foreach (var column in columns)
                {
                    AppendColumn(sb, column);
                }
            }

            sb.Append("  </table>\n</changeUnit>\n");

            var path = Path.Combine(directory, BaselineFileName);
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return path;
        }

        private static void AppendFallbackColumns(StringBuilder sb)
        {
            sb.Append("    <column name=\"id\" action=\"ADD\" type=\"Int64\" primaryKey=\"true\" autoIncrement=\"true\" nullable=\"false\"/>\n");
            sb.Append("    <column name=\"msg\" action=\"ADD\" type=\"Text\" nullable=\"true\" comment=\"消息\"/>\n");
        }

        private static void AppendColumn(StringBuilder sb, ColumnSnapshot column)
        {
            var type = MapLogicalType(column);

            sb.Append("    <column name=\"").Append(column.Name)
              .Append("\" action=\"ADD\" type=\"").Append(type).Append('"');

            if (column.Length.HasValue && RequiresLength(type))
            {
                sb.Append(" length=\"").Append(column.Length.Value).Append('"');
            }

            if (type == LogicalType.Decimal)
            {
                if (column.Precision.HasValue)
                {
                    sb.Append(" precision=\"").Append(column.Precision.Value).Append('"');
                }
                if (column.Scale.HasValue)
                {
                    sb.Append(" scale=\"").Append(column.Scale.Value).Append('"');
                }
            }

            sb.Append(" nullable=\"").Append(column.Nullable ? "true" : "false").Append('"');

            if (column.PrimaryKey)
            {
                sb.Append(" primaryKey=\"true\"");
            }
            if (column.AutoIncrement)
            {
                sb.Append(" autoIncrement=\"true\"");
            }
            if (column.IsUnsigned)
            {
                sb.Append(" unsigned=\"true\"");
            }
            if (column.HasDefault)
            {
                sb.Append(" defaultValue=\"").Append(EscapeXml(FormatDefaultLiteral(column))).Append('"');
            }
            if (!string.IsNullOrEmpty(column.Comment))
            {
                sb.Append(" comment=\"").Append(EscapeXml(column.Comment)).Append('"');
            }
            if (SupportsCharset(type) && !string.IsNullOrEmpty(column.Charset))
            {
                sb.Append(" charset=\"").Append(column.Charset).Append('"');
                if (!string.IsNullOrEmpty(column.Collate))
                {
                    sb.Append(" collate=\"").Append(column.Collate).Append('"');
                }
            }

            sb.Append("/>\n");
        }

        private static bool RequiresLength(LogicalType type)
        {
            return type == LogicalType.String
                || type == LogicalType.FixedString
                || type == LogicalType.Binary;
        }

        private static bool SupportsCharset(LogicalType type)
        {
            return type == LogicalType.String
                || type == LogicalType.FixedString
                || type == LogicalType.Text;
        }

        private static LogicalType MapLogicalType(ColumnSnapshot column)
        {
            switch ((column.DataType ?? "").ToLowerInvariant())
            {
                case "tinyint":
                    return column.ColumnType.IndexOf("tinyint(1)", StringComparison.OrdinalIgnoreCase) >= 0
                        ? LogicalType.Boolean
                        : LogicalType.Int8;
                case "smallint":
                    return LogicalType.Int16;
                case "mediumint":
                case "int":
                case "integer":
                    return LogicalType.Int32;
                case "bigint":
                    return LogicalType.Int64;
                case "decimal":
                case "numeric":
                    return LogicalType.Decimal;
                case "float":
                    return LogicalType.Float32;
                case "double":
                    return LogicalType.Float64;
                case "char":
                    return LogicalType.FixedString;
                case "varchar":
                    return LogicalType.String;
                case "tinytext":
                case "text":
                case "mediumtext":
                case "longtext":
                    return LogicalType.Text;
                case "json":
                    return LogicalType.Json;
                case "date":
                    return LogicalType.Date;
                case "time":
                    return LogicalType.Time;
                case "datetime":
                case "timestamp":
                    return LogicalType.DateTime;
                case "binary":
                case "varbinary":
                    return LogicalType.Binary;
                case "bit":
                    return LogicalType.Boolean;
                default:
                    return LogicalType.Text;
            }
        }

        private static string FormatDefaultLiteral(ColumnSnapshot column)
        {
            var value = column.Default ?? "";
            var dataType = column.DataType ?? "";

            if (value.Length == 0)
            {
                return IsNumericType(dataType) ? "0" : "''";
            }

            if (value.StartsWith("CURRENT_TIMESTAMP", StringComparison.OrdinalIgnoreCase)
                || value.Equals("NULL", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            if (IsNumericType(dataType) && decimal.TryParse(value, out _))
            {
                return value;
            }

            return "'" + value.Replace("'", "''") + "'";
        }

        private static bool IsNumericType(string dataType)
        {
            return dataType.IndexOf("int", StringComparison.OrdinalIgnoreCase) >= 0
                || dataType.IndexOf("decimal", StringComparison.OrdinalIgnoreCase) >= 0
                || dataType.IndexOf("double", StringComparison.OrdinalIgnoreCase) >= 0
                || dataType.IndexOf("float", StringComparison.OrdinalIgnoreCase) >= 0
                || dataType.IndexOf("bit", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string EscapeXml(string value)
        {
            return (value ?? "")
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }
    }
}

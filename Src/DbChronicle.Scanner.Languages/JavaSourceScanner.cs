using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

using DbChronicle.Cli.Plugins;
using DbChronicle.Common.Enums;
using DbChronicle.Models;

namespace DbChronicle.Scanner.Languages
{
    /// <summary>
    /// Java 实体扫描（JPA 风格注解）：@Table / @Column / @Id / @MaxLength / @Transient。
    /// 轻量行扫描，不做完整语法分析；复杂继承与常量引用需人工确认。
    /// </summary>
    public sealed class JavaSourceScanner : ISourceScanner
    {
        public string Language
        {
            get { return "java"; }
        }

        private static readonly Regex ClassPattern = new Regex(
            @"(?:public\s+)?(?:final\s+)?class\s+(?<name>\w+)", RegexOptions.Compiled);

        private static readonly Regex TablePattern = new Regex(
            @"@Table\s*\(\s*(?:name\s*=\s*)?""(?<name>[^""]+)""", RegexOptions.Compiled);

        private static readonly Regex ColumnPattern = new Regex(
            @"@Column\s*\(\s*(?:name\s*=\s*)?""(?<name>[^""]+)""", RegexOptions.Compiled);

        private static readonly Regex MaxLengthPattern = new Regex(
            @"@(?:MaxLength|Size|Length)\s*\(\s*(?:max\s*=\s*)?(?<len>\d+)", RegexOptions.Compiled);

        private static readonly Regex FieldPattern = new Regex(
            @"^\s*(?:private|protected|public)\s+(?:final\s+)?(?<type>[\w.<>\[\]]+)\s+(?<name>\w+)\s*(?:=[^;]+)?;",
            RegexOptions.Compiled);

        public ScanResult ScanDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException("Java 源码目录不存在：" + directory);
            }

            var result = new ScanResult();
            foreach (var file in Directory.GetFiles(directory, "*.java", SearchOption.AllDirectories))
            {
                if (file.Replace('\\', '/').Contains("/target/"))
                {
                    continue;
                }
                ScanFile(file, result);
            }
            return result;
        }

        private void ScanFile(string path, ScanResult result)
        {
            var lines = File.ReadAllLines(path);
            ScannedTable current = null;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                var classMatch = ClassPattern.Match(line);
                if (classMatch.Success && !line.Contains("("))
                {
                    if (current != null && current.Columns.Count > 0)
                    {
                        result.Tables.Add(current);
                    }

                    current = new ScannedTable { Name = classMatch.Groups["name"].Value };
                    // 类上方的 @Table 注解
                    for (var back = i - 1; back >= 0 && back >= i - 6; back--)
                    {
                        var tableMatch = TablePattern.Match(lines[back]);
                        if (tableMatch.Success)
                        {
                            current.Name = tableMatch.Groups["name"].Value;
                            break;
                        }
                        if (lines[back].Trim().Length == 0)
                        {
                            break;
                        }
                    }
                    continue;
                }

                if (current == null || line.Contains("(") || line.Contains(")"))
                {
                    continue;
                }

                var fieldMatch = FieldPattern.Match(line);
                if (!fieldMatch.Success)
                {
                    continue;
                }

                var typeName = fieldMatch.Groups["type"].Value;
                var fieldName = fieldMatch.Groups["name"].Value;
                if (typeName == "static" || typeName == "final")
                {
                    continue;
                }

                var annotations = new List<string>();
                for (var back = i - 1; back >= 0 && back >= i - 5; back--)
                {
                    var text = lines[back].Trim();
                    if (text.Length == 0 || text.StartsWith("//"))
                    {
                        break;
                    }
                    annotations.Add(text);
                }
                var joined = string.Join(" ", annotations);

                if (JoinedContains(joined, "@Transient") || JoinedContains(joined, "@JsonIgnore"))
                {
                    continue;
                }

                var columnName = fieldName;
                var columnMatch = ColumnPattern.Match(joined);
                if (columnMatch.Success)
                {
                    columnName = columnMatch.Groups["name"].Value;
                }

                var column = new ScannedColumn
                {
                    Name = columnName,
                    Nullable = !IsPrimitive(typeName),
                    PrimaryKey = JoinedContains(joined, "@Id") || string.Equals(fieldName, "id", StringComparison.OrdinalIgnoreCase),
                    AutoIncrement = JoinedContains(joined, "@GeneratedValue")
                };

                var lengthMatch = MaxLengthPattern.Match(joined);
                if (lengthMatch.Success)
                {
                    column.Length = int.Parse(lengthMatch.Groups["len"].Value);
                }

                MapType(typeName, column);
                current.Columns.Add(column);
                if (column.PrimaryKey)
                {
                    current.PrimaryKeyColumns.Add(columnName);
                }
            }

            if (current != null && current.Columns.Count > 0)
            {
                result.Tables.Add(current);
            }
        }

        private static bool JoinedContains(string text, string token)
        {
            return text.IndexOf(token, StringComparison.Ordinal) >= 0;
        }

        private static bool IsPrimitive(string typeName)
        {
            switch (typeName)
            {
                case "long":
                case "int":
                case "short":
                case "byte":
                case "boolean":
                case "float":
                case "double":
                    return true;
                default:
                    return false;
            }
        }

        private static void MapType(string typeName, ScannedColumn column)
        {
            switch (typeName)
            {
                case "Long":
                case "long":
                    column.Type = LogicalType.Int64;
                    return;
                case "Integer":
                case "int":
                    column.Type = LogicalType.Int32;
                    return;
                case "Short":
                case "short":
                    column.Type = LogicalType.Int16;
                    return;
                case "Byte":
                case "byte":
                    column.Type = LogicalType.Int8;
                    return;
                case "Boolean":
                case "boolean":
                    column.Type = LogicalType.Boolean;
                    return;
                case "Float":
                case "float":
                    column.Type = LogicalType.Float32;
                    return;
                case "Double":
                case "double":
                    column.Type = LogicalType.Float64;
                    return;
                case "BigDecimal":
                    column.Type = LogicalType.Decimal;
                    return;
                case "LocalDateTime":
                case "Date":
                case "Instant":
                    column.Type = LogicalType.DateTime;
                    return;
                case "OffsetDateTime":
                case "ZonedDateTime":
                    column.Type = LogicalType.DateTimeOffset;
                    return;
                case "LocalDate":
                    column.Type = LogicalType.Date;
                    return;
                case "LocalTime":
                    column.Type = LogicalType.Time;
                    return;
                case "UUID":
                    column.Type = LogicalType.Uuid;
                    return;
                case "byte[]":
                    column.Type = LogicalType.Binary;
                    return;
                case "String":
                    column.Type = column.Length.HasValue ? LogicalType.String : LogicalType.Text;
                    return;
                default:
                    column.Type = LogicalType.Text;
                    return;
            }
        }
    }
}

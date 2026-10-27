using System;
using System.IO;
using System.Text.RegularExpressions;

using DbChronicle.Common.Enums;
using DbChronicle.Models;

namespace DbChronicle.Scanner.Languages
{
    /// <summary>
    /// Go 实体扫描：struct 定义 + `db:"列名"` tag。
    /// 只识别带 db tag 的导出字段（没有 tag 的字段视为非持久化字段）。
    /// </summary>
    public sealed class GoSourceScanner
    {
        private static readonly Regex StructPattern = new Regex(
            @"type\s+(?<name>\w+)\s+struct\s*\{(?<body>[^{}]*)\}",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex FieldPattern = new Regex(
            @"^\s*(?<name>[A-Z]\w*)\s+(?<type>[\w\.\[\]\*]+)(?:\s+`(?<tag>[^`]*)`)?\s*$",
            RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex DbTagPattern = new Regex(@"db:""(?<col>[^"",]+)", RegexOptions.Compiled);

        public ScanResult ScanDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException("Go 源码目录不存在：" + directory);
            }

            var result = new ScanResult();
            foreach (var file in Directory.GetFiles(directory, "*.go", SearchOption.AllDirectories))
            {
                if (file.EndsWith("_test.go", StringComparison.OrdinalIgnoreCase)
                    || file.Replace('\\', '/').Contains("/vendor/"))
                {
                    continue;
                }
                ScanFile(file, result);
            }
            return result;
        }

        private void ScanFile(string path, ScanResult result)
        {
            var text = File.ReadAllText(path);

            foreach (Match structMatch in StructPattern.Matches(text))
            {
                var body = structMatch.Groups["body"].Value;
                var table = new ScannedTable { Name = structMatch.Groups["name"].Value };

                foreach (Match fieldMatch in FieldPattern.Matches(body))
                {
                    var tag = fieldMatch.Groups["tag"].Value;
                    var dbTag = DbTagPattern.Match(tag);
                    if (!dbTag.Success)
                    {
                        continue;
                    }

                    var column = new ScannedColumn
                    {
                        Name = dbTag.Groups["col"].Value,
                        Nullable = IsPointerOrSlice(fieldMatch.Groups["type"].Value),
                        PrimaryKey = IsPrimary(fieldMatch.Groups["name"].Value)
                    };

                    MapType(fieldMatch.Groups["type"].Value, column);
                    table.Columns.Add(column);
                    if (column.PrimaryKey)
                    {
                        table.PrimaryKeyColumns.Add(column.Name);
                    }
                }

                if (table.Columns.Count > 0)
                {
                    result.Tables.Add(table);
                }
            }
        }

        private static bool IsPrimary(string fieldName)
        {
            return string.Equals(fieldName, "ID", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fieldName, "Id", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPointerOrSlice(string typeName)
        {
            return typeName.StartsWith("*", StringComparison.Ordinal) || typeName.StartsWith("[]", StringComparison.Ordinal);
        }

        private static void MapType(string typeName, ScannedColumn column)
        {
            switch (typeName)
            {
                case "int64":
                    column.Type = LogicalType.Int64;
                    return;
                case "int32":
                case "int":
                    column.Type = LogicalType.Int32;
                    return;
                case "int16":
                    column.Type = LogicalType.Int16;
                    return;
                case "int8":
                    column.Type = LogicalType.Int8;
                    return;
                case "uint64":
                    column.Type = LogicalType.Int64;
                    column.Unsigned = true;
                    return;
                case "uint32":
                case "uint":
                    column.Type = LogicalType.Int32;
                    column.Unsigned = true;
                    return;
                case "uint16":
                    column.Type = LogicalType.Int16;
                    column.Unsigned = true;
                    return;
                case "uint8":
                case "byte":
                    column.Type = LogicalType.Int8;
                    column.Unsigned = true;
                    return;
                case "bool":
                    column.Type = LogicalType.Boolean;
                    return;
                case "float32":
                    column.Type = LogicalType.Float32;
                    return;
                case "float64":
                    column.Type = LogicalType.Float64;
                    return;
                case "time.Time":
                    column.Type = LogicalType.DateTime;
                    return;
                case "*time.Time":
                    column.Type = LogicalType.DateTime;
                    column.Nullable = true;
                    return;
                case "[]byte":
                    column.Type = LogicalType.Blob;
                    return;
                case "string":
                case "*string":
                    column.Type = LogicalType.Text;
                    return;
                default:
                    column.Type = LogicalType.Text;
                    return;
            }
        }
    }
}
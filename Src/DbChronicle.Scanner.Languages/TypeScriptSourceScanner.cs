using System;
using System.IO;
using System.Text.RegularExpressions;

using DbChronicle.Common.Enums;
using DbChronicle.Models;

namespace DbChronicle.Scanner.Languages
{
    /// <summary>
    /// TypeScript 实体扫描：支持 `export interface X { id: number }` 与 TypeORM 风格
    /// `@Entity('t') class X { @Column({ name: 'c' }) id: number }` 两种写法。
    /// </summary>
    public sealed class TypeScriptSourceScanner
    {
        private static readonly Regex EntityDecoratorPattern = new Regex(
            @"@Entity\s*\(\s*(?:name\s*:\s*)?['""](?<name>[^'""]+)['""]", RegexOptions.Compiled);

        private static readonly Regex DeclarePattern = new Regex(
            @"export\s+(?:default\s+)?(?:class|interface)\s+(?<name>\w+)",
            RegexOptions.Compiled);

        private static readonly Regex MemberPattern = new Regex(
            @"^\s*(?:readonly\s+)?(?<name>\w+)(?<optional>\?)?\s*:\s*(?<type>[\w<>\[\]| .']+?)\s*(?:;|,)?\s*$",
            RegexOptions.Compiled);

        private static readonly Regex ColumnDecoratorPattern = new Regex(
            @"@(?:Column|PrimaryColumn|PrimaryGeneratedColumn)\s*\((?<args>[^)]*)\)",
            RegexOptions.Compiled);

        public ScanResult ScanDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException("TypeScript 源码目录不存在：" + directory);
            }

            var result = new ScanResult();
            foreach (var file in Directory.GetFiles(directory, "*.ts", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase)
                    || file.Replace('\\', '/').Contains("/node_modules/"))
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
            var depth = 0;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.Trim();

                var declare = DeclarePattern.Match(line);
                if (declare.Success && !trimmed.Contains("("))
                {
                    if (current != null && current.Columns.Count > 0)
                    {
                        result.Tables.Add(current);
                    }

                    current = new ScannedTable { Name = declare.Groups["name"].Value };
                    // 上方 6 行内的 @Entity('t')
                    for (var back = i - 1; back >= 0 && back >= i - 6; back--)
                    {
                        var entity = EntityDecoratorPattern.Match(lines[back]);
                        if (entity.Success)
                        {
                            current.Name = entity.Groups["name"].Value;
                            break;
                        }
                        if (lines[back].Trim().Length == 0)
                        {
                            break;
                        }
                    }
                    depth = 0;
                    continue;
                }

                if (current == null)
                {
                    continue;
                }

                depth += Count(line, '{') - Count(line, '}');
                if (depth < 0)
                {
                    if (current.Columns.Count > 0)
                    {
                        result.Tables.Add(current);
                    }
                    current = null;
                    continue;
                }

                if (trimmed.StartsWith("//", StringComparison.Ordinal)) 
                {
                    continue;
                }

                var member = MemberPattern.Match(line);
                if (!member.Success)
                {
                    continue;
                }

                var propertyName = member.Groups["name"].Value;
                if (propertyName == "constructor")
                {
                    continue;
                }

                var tsType = member.Groups["type"].Value.Trim();
                var optional = member.Groups["optional"].Success || tsType.EndsWith("null", StringComparison.Ordinal);

                var columnName = propertyName;
                var length = (int?)null;
                var isPrimary = propertyName == "id" || propertyName == "Id";

                // 上方 5 行内的列装饰器
                var decoratorText = "";
                for (var back = i - 1; back >= 0 && back >= i - 5; back--)
                {
                    var text = lines[back].Trim();
                    if (text.StartsWith("@", StringComparison.Ordinal))
                    {
                        decoratorText += " " + text;
                        continue;
                    }
                    break;
                }

                var columnDecorator = ColumnDecoratorPattern.Match(decoratorText);
                if (columnDecorator.Success)
                {
                    var args = columnDecorator.Groups["args"].Value;
                    var nameMatch = Regex.Match(args, @"name\s*:\s*['""](?<col>[^'""]+)['""]");
                    if (nameMatch.Success)
                    {
                        columnName = nameMatch.Groups["col"].Value;
                    }
                    var lengthMatch = Regex.Match(args, @"length\s*:\s*(?<len>\d+)");
                    if (lengthMatch.Success)
                    {
                        length = int.Parse(lengthMatch.Groups["len"].Value);
                    }
                    if (decoratorText.IndexOf("@PrimaryColumn", StringComparison.Ordinal) >= 0
                        || decoratorText.IndexOf("@PrimaryGeneratedColumn", StringComparison.Ordinal) >= 0)
                    {
                        isPrimary = true;
                    }
                }

                var column = new ScannedColumn
                {
                    Name = columnName,
                    Nullable = optional,
                    PrimaryKey = isPrimary,
                    AutoIncrement = decoratorText.IndexOf("@PrimaryGeneratedColumn", StringComparison.Ordinal) >= 0,
                    Length = length
                };

                MapType(tsType, column);
                current.Columns.Add(column);
                if (isPrimary)
                {
                    current.PrimaryKeyColumns.Add(columnName);
                }
            }

            if (current != null && current.Columns.Count > 0)
            {
                result.Tables.Add(current);
            }
        }

        private static int Count(string text, char token)
        {
            var count = 0;
            foreach (var ch in text)
            {
                if (ch == token)
                {
                    count++;
                }
            }
            return count;
        }

        private static void MapType(string tsType, ScannedColumn column)
        {
            var normalized = tsType.Replace(" ", "").Replace("|null", "").Replace("null|", "");

            switch (normalized)
            {
                case "boolean":
                    column.Type = LogicalType.Boolean;
                    return;
                case "number":
                    column.Type = LogicalType.Int64;
                    return;
                case "bigint":
                    column.Type = LogicalType.Int64;
                    return;
                case "Date":
                    column.Type = LogicalType.DateTime;
                    return;
                case "Uint8Array":
                case "Buffer":
                    column.Type = LogicalType.Blob;
                    return;
                case "string":
                    column.Type = column.Length.HasValue ? LogicalType.String : LogicalType.Text;
                    return;
                default:
                    column.Type = LogicalType.Text;
                    return;
            }
        }
    }
}
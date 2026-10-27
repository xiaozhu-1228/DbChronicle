using System.Collections.Generic;
using System.Text;

using DbChronicle.Common.Enums;
using DbChronicle.Models;

namespace DbChronicle.CodeGen
{
    /// <summary>C# 实体生成：DataAnnotations + [Table]/[Column]，可直接给 EF Core / Dapper 用。</summary>
    public sealed class CSharpEntityWriter : IEntityCodeWriter
    {
        public string Language => "csharp";

        public string FileExtension => ".cs";

        public string WriteTable(TableModel table, EntityCodeOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// 由 DbChronicle 代码生成（XML → 实体），请勿手工修改；结构变更请修改 migrations/");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.ComponentModel.DataAnnotations;");
            sb.AppendLine("using System.ComponentModel.DataAnnotations.Schema;");
            sb.AppendLine();
            sb.AppendLine("namespace " + options.Namespace);
            sb.AppendLine("{");

            var className = CodeNaming.ToPascalCase(table.Name);
            if (options.IncludeComments && !string.IsNullOrEmpty(table.Comment))
            {
                sb.AppendLine("    /// <summary>" + Comment(table.Comment) + "</summary>");
            }
            sb.AppendLine("    [Table(\"" + table.Name + "\")]");
            sb.AppendLine("    public class " + className);
            sb.AppendLine("    {");

            foreach (var columnName in table.ColumnOrder)
            {
                ColumnDefinition column;
                if (!table.Columns.TryGetValue(columnName, out column))
                {
                    continue;
                }

                var propertyName = CodeNaming.EscapeIdentifier(CodeNaming.ToPascalCase(column.Name), true);
                if (options.IncludeComments && !string.IsNullOrEmpty(column.Comment))
                {
                    sb.AppendLine("        /// <summary>" + Comment(column.Comment) + "</summary>");
                }

                var isKey = table.PrimaryKeyColumns.Exists(x => string.Equals(x, column.Name, System.StringComparison.OrdinalIgnoreCase))
                    || column.PrimaryKey.GetValueOrDefault();
                if (isKey)
                {
                    sb.AppendLine("        [Key]");
                }
                if (options.IncludeColumnMapping)
                {
                    sb.AppendLine("        [Column(\"" + column.Name + "\")]");
                }
                if (column.Length.HasValue && IsTextType(column.Type))
                {
                    sb.AppendLine("        [MaxLength(" + column.Length.Value + ")]");
                }
                if (!column.Nullable.GetValueOrDefault() && IsTextType(column.Type))
                {
                    sb.AppendLine("        [Required]");
                }

                sb.AppendLine("        public " + ClrType(column) + " " + propertyName + " { get; set; }");
                sb.AppendLine();
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public IEnumerable<KeyValuePair<string, string>> WriteExtraFiles(EntityCodeOptions options)
        {
            yield break;
        }

        private static bool IsTextType(LogicalType? type)
        {
            return type == LogicalType.String || type == LogicalType.FixedString || type == LogicalType.Text
                || type == LogicalType.Json || type == LogicalType.Xml;
        }

        private static string ClrType(ColumnDefinition column)
        {
            string type;
            switch (column.Type.GetValueOrDefault())
            {
                case LogicalType.Boolean:
                    type = "bool";
                    break;
                case LogicalType.Int8:
                    type = column.Unsigned.GetValueOrDefault() ? "byte" : "sbyte";
                    break;
                case LogicalType.Int16:
                    type = column.Unsigned.GetValueOrDefault() ? "ushort" : "short";
                    break;
                case LogicalType.Int32:
                    type = column.Unsigned.GetValueOrDefault() ? "uint" : "int";
                    break;
                case LogicalType.Int64:
                    type = column.Unsigned.GetValueOrDefault() ? "ulong" : "long";
                    break;
                case LogicalType.Float32:
                    type = "float";
                    break;
                case LogicalType.Float64:
                    type = "double";
                    break;
                case LogicalType.Decimal:
                    type = "decimal";
                    break;
                case LogicalType.DateTime:
                    type = "DateTime";
                    break;
                case LogicalType.DateTimeOffset:
                    type = "DateTimeOffset";
                    break;
                case LogicalType.Date:
                    type = "DateOnly";
                    break;
                case LogicalType.Time:
                    type = "TimeOnly";
                    break;
                case LogicalType.Uuid:
                    type = "Guid";
                    break;
                case LogicalType.Binary:
                case LogicalType.Blob:
                    return "byte[]";
                default:
                    type = "string";
                    break;
            }

            // 统一用可空标注（文件头已开启 #nullable enable），保留数据库的可空语义
            return column.Nullable.GetValueOrDefault() ? type + "?" : type;
        }

        private static string Comment(string text)
        {
            return (text ?? "").Replace("\r", " ").Replace("\n", " ").Replace("--", "- -");
        }
    }
}

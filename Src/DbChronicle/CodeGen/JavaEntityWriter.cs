using System.Collections.Generic;
using System.Text;

using DbChronicle.Common.Enums;
using DbChronicle.Models;

namespace DbChronicle.CodeGen
{
    /// <summary>Java POJO 生成：字段 + getter/setter，注释里标注列名。</summary>
    public sealed class JavaEntityWriter : IEntityCodeWriter
    {
        public string Language => "java";

        public string FileExtension => ".java";

        public string WriteTable(TableModel table, EntityCodeOptions options)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(options.Namespace))
            {
                sb.AppendLine("package " + options.Namespace + ";");
                sb.AppendLine();
            }

            if (NeedsBigDecimal(table))
            {
                sb.AppendLine("import java.math.BigDecimal;");
            }
            sb.AppendLine("import java.time.LocalDate;");
            sb.AppendLine("import java.time.LocalDateTime;");
            sb.AppendLine("import java.time.LocalTime;");
            sb.AppendLine("import java.time.OffsetDateTime;");
            sb.AppendLine("import java.util.UUID;");
            sb.AppendLine();

            var className = CodeNaming.ToPascalCase(table.Name);
            if (options.IncludeComments && !string.IsNullOrEmpty(table.Comment))
            {
                sb.AppendLine("/** " + Comment(table.Comment) + "（表 " + table.Name + "） */");
            }
            sb.AppendLine("public class " + className + " {");

            foreach (var columnName in table.ColumnOrder)
            {
                ColumnDefinition column;
                if (!table.Columns.TryGetValue(columnName, out column))
                {
                    continue;
                }

                if (options.IncludeComments)
                {
                    var note = string.IsNullOrEmpty(column.Comment) ? "列 " + column.Name : column.Comment + "（列 " + column.Name + "）";
                    sb.AppendLine("    /** " + Comment(note) + " */");
                }

                var fieldName = CodeNaming.EscapeIdentifier(CodeNaming.ToCamelCase(column.Name), false);
                var javaType = JavaType(column);
                sb.AppendLine("    private " + javaType + " " + fieldName + ";");
                sb.AppendLine();
                sb.AppendLine("    public " + javaType + " get" + CodeNaming.ToPascalCase(column.Name) + "() {");
                sb.AppendLine("        return " + fieldName + ";");
                sb.AppendLine("    }");
                sb.AppendLine();
                sb.AppendLine("    public void set" + CodeNaming.ToPascalCase(column.Name) + "(" + javaType + " " + fieldName + ") {");
                sb.AppendLine("        this." + fieldName + " = " + fieldName + ";");
                sb.AppendLine("    }");
                sb.AppendLine();
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        public IEnumerable<KeyValuePair<string, string>> WriteExtraFiles(EntityCodeOptions options)
        {
            yield break;
        }

        private static bool NeedsBigDecimal(TableModel table)
        {
            foreach (var column in table.Columns.Values)
            {
                if (column.Type == LogicalType.Decimal)
                {
                    return true;
                }
            }
            return false;
        }

        private static string JavaType(ColumnDefinition column)
        {
            switch (column.Type.GetValueOrDefault())
            {
                case LogicalType.Boolean:
                    return "Boolean";
                case LogicalType.Int8:
                    return "Byte";
                case LogicalType.Int16:
                    return "Short";
                case LogicalType.Int32:
                    return "Integer";
                case LogicalType.Int64:
                    return "Long";
                case LogicalType.Float32:
                    return "Float";
                case LogicalType.Float64:
                    return "Double";
                case LogicalType.Decimal:
                    return "BigDecimal";
                case LogicalType.DateTime:
                    return "LocalDateTime";
                case LogicalType.DateTimeOffset:
                    return "OffsetDateTime";
                case LogicalType.Date:
                    return "LocalDate";
                case LogicalType.Time:
                    return "LocalTime";
                case LogicalType.Uuid:
                    return "UUID";
                case LogicalType.Binary:
                case LogicalType.Blob:
                    return "byte[]";
                default:
                    return "String";
            }
        }

        private static string Comment(string text)
        {
            return (text ?? "").Replace("\r", " ").Replace("\n", " ").Replace("*/", "* /");
        }
    }
}

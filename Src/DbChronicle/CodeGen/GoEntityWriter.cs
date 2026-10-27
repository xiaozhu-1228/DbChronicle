using System.Collections.Generic;
using System.Text;

using DbChronicle.Common.Enums;
using DbChronicle.Models;

namespace DbChronicle.CodeGen
{
    /// <summary>Go struct 生成：字段带 db tag，时间用 time.Time。</summary>
    public sealed class GoEntityWriter : IEntityCodeWriter
    {
        public string Language => "go";

        public string FileExtension => ".go";

        public string WriteTable(TableModel table, EntityCodeOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine("package " + (string.IsNullOrEmpty(options.Namespace) ? "entities" : options.Namespace));
            sb.AppendLine();
            sb.AppendLine("import \"time\"");
            sb.AppendLine();

            if (options.IncludeComments && !string.IsNullOrEmpty(table.Comment))
            {
                sb.AppendLine("// " + CodeNaming.ToPascalCase(table.Name) + " " + Comment(table.Comment) + "（表 " + table.Name + "）");
            }
            sb.AppendLine("type " + CodeNaming.ToGoExported(table.Name) + " struct {");

            foreach (var columnName in table.ColumnOrder)
            {
                ColumnDefinition column;
                if (!table.Columns.TryGetValue(columnName, out column))
                {
                    continue;
                }

                var fieldName = CodeNaming.ToGoExported(column.Name);
                var tag = options.IncludeColumnMapping
                    ? " `db:\"" + column.Name + "\" json:\"" + CodeNaming.ToCamelCase(column.Name) + "\"`"
                    : "";
                var comment = options.IncludeComments && !string.IsNullOrEmpty(column.Comment)
                    ? " // " + Comment(column.Comment)
                    : "";

                sb.AppendLine("\t" + fieldName + " " + GoType(column) + tag + comment);
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        public IEnumerable<KeyValuePair<string, string>> WriteExtraFiles(EntityCodeOptions options)
        {
            yield break;
        }

        private static string GoType(ColumnDefinition column)
        {
            switch (column.Type.GetValueOrDefault())
            {
                case LogicalType.Boolean:
                    return "bool";
                case LogicalType.Int8:
                    return column.Unsigned.GetValueOrDefault() ? "uint8" : "int8";
                case LogicalType.Int16:
                    return column.Unsigned.GetValueOrDefault() ? "uint16" : "int16";
                case LogicalType.Int32:
                    return column.Unsigned.GetValueOrDefault() ? "uint32" : "int32";
                case LogicalType.Int64:
                    return column.Unsigned.GetValueOrDefault() ? "uint64" : "int64";
                case LogicalType.Float32:
                    return "float32";
                case LogicalType.Float64:
                case LogicalType.Decimal:
                    return "float64";
                case LogicalType.DateTime:
                case LogicalType.DateTimeOffset:
                case LogicalType.Date:
                case LogicalType.Time:
                    return "time.Time";
                case LogicalType.Binary:
                case LogicalType.Blob:
                    return "[]byte";
                default:
                    return "string";
            }
        }

        private static string Comment(string text)
        {
            return (text ?? "").Replace("\r", " ").Replace("\n", " ");
        }
    }
}

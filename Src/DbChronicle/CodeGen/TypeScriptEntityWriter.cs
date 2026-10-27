using System.Collections.Generic;
using System.Text;

using DbChronicle.Common.Enums;
using DbChronicle.Models;

namespace DbChronicle.CodeGen
{
    /// <summary>TypeScript 接口生成：字段为 camelCase，注释里标注列名与可空性。</summary>
    public sealed class TypeScriptEntityWriter : IEntityCodeWriter
    {
        public string Language => "typescript";

        public string FileExtension => ".ts";

        public string WriteTable(TableModel table, EntityCodeOptions options)
        {
            var sb = new StringBuilder();
            var interfaceName = CodeNaming.ToPascalCase(table.Name);

            if (options.IncludeComments)
            {
                sb.AppendLine("/**");
                sb.AppendLine(" * " + (string.IsNullOrEmpty(table.Comment) ? "表 " + table.Name : Comment(table.Comment))
                    + "（表 " + table.Name + "）");
                sb.AppendLine(" */");
            }

            sb.AppendLine("export interface " + interfaceName + " {");

            foreach (var columnName in table.ColumnOrder)
            {
                ColumnDefinition column;
                if (!table.Columns.TryGetValue(columnName, out column))
                {
                    continue;
                }

                if (options.IncludeComments)
                {
                    var nullableText = column.Nullable.GetValueOrDefault() ? " | null" : "";
                    sb.AppendLine("  /** " + Comment("列 " + column.Name + (string.IsNullOrEmpty(column.Comment) ? "" : "：" + column.Comment))
                        + nullableText + " */");
                }

                var propertyName = CodeNaming.EscapeIdentifier(CodeNaming.ToCamelCase(column.Name), false);
                var optional = column.Nullable.GetValueOrDefault() ? " | null" : "";
                sb.AppendLine("  " + propertyName + ": " + TsType(column) + optional + ";");
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        public IEnumerable<KeyValuePair<string, string>> WriteExtraFiles(EntityCodeOptions options)
        {
            yield break;
        }

        private static string TsType(ColumnDefinition column)
        {
            switch (column.Type.GetValueOrDefault())
            {
                case LogicalType.Boolean:
                    return "boolean";
                case LogicalType.Int8:
                case LogicalType.Int16:
                case LogicalType.Int32:
                case LogicalType.Int64:
                case LogicalType.Float32:
                case LogicalType.Float64:
                case LogicalType.Decimal:
                    return "number";
                case LogicalType.DateTime:
                case LogicalType.DateTimeOffset:
                case LogicalType.Date:
                case LogicalType.Time:
                    return "Date";
                case LogicalType.Binary:
                case LogicalType.Blob:
                    return "Uint8Array";
                default:
                    return "string";
            }
        }

        private static string Comment(string text)
        {
            return (text ?? "").Replace("\r", " ").Replace("\n", " ").Replace("*/", "* /");
        }
    }
}

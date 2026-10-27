using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using DbChronicle.Common.Enums;
using DbChronicle.Models;

namespace DbChronicle.CodeGen
{
    /// <summary>
    /// C++ 实体生成：每表一个头文件（struct + 成员注释），另生成聚合头 all_entities.h。
    /// 目标 C++17（optional / 嵌套命名空间）；可空列用 std::optional 表达，与 NULL 区分。
    /// </summary>
    public sealed class CppEntityWriter : IEntityCodeWriter
    {
        /// <summary>C++ 关键字（成员名冲突时追加下划线，避免生成无法编译的代码）。</summary>
        private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "alignas", "alignof", "and", "and_eq", "asm", "auto", "bitand", "bitor", "bool", "break",
            "case", "catch", "char", "char8_t", "char16_t", "char32_t", "class", "compl", "concept",
            "const", "consteval", "constexpr", "constinit", "const_cast", "continue", "co_await",
            "co_return", "co_yield", "decltype", "default", "delete", "do", "double", "dynamic_cast",
            "else", "enum", "explicit", "export", "extern", "false", "float", "for", "friend", "goto",
            "if", "inline", "int", "long", "mutable", "namespace", "new", "noexcept", "not", "not_eq",
            "nullptr", "operator", "or", "or_eq", "private", "protected", "public", "register",
            "reinterpret_cast", "requires", "return", "short", "signed", "sizeof", "static",
            "static_assert", "static_cast", "struct", "switch", "template", "this", "thread_local",
            "throw", "true", "try", "typedef", "typeid", "typename", "union", "unsigned", "using",
            "virtual", "void", "volatile", "wchar_t", "while", "xor", "xor_eq"
        };

        public string Language => "cpp";

        public string FileExtension => ".h";

        public string WriteTable(TableModel table, EntityCodeOptions options)
        {
            var structName = CodeNaming.ToPascalCase(table.Name);
            var body = new StringBuilder();
            var includes = new SortedSet<string>(StringComparer.Ordinal);

            var fields = new StringBuilder();
            foreach (var columnName in table.ColumnOrder)
            {
                ColumnDefinition column;
                if (!table.Columns.TryGetValue(columnName, out column))
                {
                    continue;
                }

                var fieldName = MemberName(column.Name);
                var isKey = table.PrimaryKeyColumns.Exists(x => string.Equals(x, column.Name, StringComparison.OrdinalIgnoreCase))
                    || column.PrimaryKey.GetValueOrDefault();
                var cppType = CppType(column, includes);

                if (options.IncludeComments)
                {
                    var notes = new List<string>();
                    if (isKey)
                    {
                        notes.Add(column.AutoIncrement.GetValueOrDefault() ? "主键，自增" : "主键");
                    }
                    if (!string.IsNullOrEmpty(column.Comment))
                    {
                        notes.Add(column.Comment);
                    }
                    notes.Add(PhysicalNote(column));
                    fields.Append("    ").Append(cppType).Append(' ').Append(fieldName).Append(DefaultInitializer(column)).Append(";");
                    fields.Append("  // ").Append(string.Join("，", notes.Where(x => !string.IsNullOrEmpty(x))));
                    fields.AppendLine();
                }
                else
                {
                    fields.Append("    ").Append(cppType).Append(' ').Append(fieldName).Append(DefaultInitializer(column)).AppendLine(";");
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("// 由 DbChronicle 代码生成（XML → C++），请勿手工修改；结构变更请修改 migrations/");
            sb.AppendLine("#pragma once");
            sb.AppendLine();
            includes.Add("cstdint");
            includes.Add("optional");
            includes.Add("string");
            foreach (var include in includes)
            {
                sb.Append("#include <").Append(include).AppendLine(">");
            }
            sb.AppendLine();
            sb.AppendLine("namespace " + (string.IsNullOrEmpty(options.Namespace) ? "entities" : options.Namespace) + " {");
            sb.AppendLine();
            if (options.IncludeComments)
            {
                sb.AppendLine("// 表 " + table.Name + (string.IsNullOrEmpty(table.Comment) ? "" : "：" + Comment(table.Comment)));
            }
            sb.AppendLine("struct " + structName + " {");
            sb.AppendLine("    static constexpr const char* kTableName = \"" + table.Name + "\";");
            sb.AppendLine();
            sb.Append(fields);
            sb.AppendLine("};");
            sb.AppendLine();
            sb.AppendLine("}  // namespace " + (string.IsNullOrEmpty(options.Namespace) ? "entities" : options.Namespace));
            return sb.ToString();
        }

        public IEnumerable<KeyValuePair<string, string>> WriteExtraFiles(EntityCodeOptions options)
        {
            yield break;
        }

        /// <summary>生成聚合头 all_entities.h，一次 include 全部实体。</summary>
        public string WriteAggregateHeader(IReadOnlyList<TableModel> tables, EntityCodeOptions options)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// 由 DbChronicle 代码生成，请勿手工修改");
            sb.AppendLine("#pragma once");
            sb.AppendLine();
            foreach (var table in tables.OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                sb.Append("#include \"").Append(CodeNaming.ToPascalCase(table.Name)).Append(".h\"").AppendLine();
            }
            return sb.ToString();
        }

        private static string MemberName(string columnName)
        {
            var name = columnName;
            if (Keywords.Contains(name))
            {
                return name + "_";
            }
            if (name.Length > 0 && char.IsDigit(name[0]))
            {
                return "_" + name;
            }
            return name;
        }

        private static string CppType(ColumnDefinition column, ISet<string> includes)
        {
            string type;
            switch (column.Type.GetValueOrDefault())
            {
                case LogicalType.Boolean:
                    type = "bool";
                    break;
                case LogicalType.Int8:
                    type = column.Unsigned.GetValueOrDefault() ? "std::uint8_t" : "std::int8_t";
                    break;
                case LogicalType.Int16:
                    type = column.Unsigned.GetValueOrDefault() ? "std::uint16_t" : "std::int16_t";
                    break;
                case LogicalType.Int32:
                    type = column.Unsigned.GetValueOrDefault() ? "std::uint32_t" : "std::int32_t";
                    break;
                case LogicalType.Int64:
                    type = column.Unsigned.GetValueOrDefault() ? "std::uint64_t" : "std::int64_t";
                    break;
                case LogicalType.Float32:
                    type = "float";
                    break;
                case LogicalType.Float64:
                    type = "double";
                    break;
                case LogicalType.Decimal:
                    // C++ 无内置十进制类型；用 double 并在注释里标注精度损失
                    type = "double";
                    break;
                case LogicalType.DateTime:
                case LogicalType.DateTimeOffset:
                    includes.Add("chrono");
                    type = "std::chrono::system_clock::time_point";
                    break;
                case LogicalType.Date:
                case LogicalType.Time:
                    // 日期/时间用 ISO 8601 字符串，避免 C++20 chrono 日历类型的可移植性问题
                    type = "std::string";
                    break;
                case LogicalType.Uuid:
                    type = "std::string";
                    break;
                case LogicalType.Binary:
                case LogicalType.Blob:
                    includes.Add("vector");
                    type = "std::vector<std::uint8_t>";
                    break;
                default:
                    type = "std::string";
                    break;
            }

            if (column.Nullable.GetValueOrDefault())
            {
                return "std::optional<" + type + ">";
            }
            return type;
        }

        private static string DefaultInitializer(ColumnDefinition column)
        {
            var nullable = column.Nullable.GetValueOrDefault();
            switch (column.Type.GetValueOrDefault())
            {
                case LogicalType.Boolean:
                    return nullable ? "" : " = false";
                case LogicalType.Int8:
                case LogicalType.Int16:
                case LogicalType.Int32:
                case LogicalType.Int64:
                    return nullable ? "" : " = 0";
                case LogicalType.Float32:
                case LogicalType.Float64:
                    return nullable ? "" : " = 0.0f";
                case LogicalType.Decimal:
                    return nullable ? "" : " = 0.0";
                default:
                    return "";
            }
        }

        private static string PhysicalNote(ColumnDefinition column)
        {
            var text = column.Type.ToString();
            if (column.Length.HasValue)
            {
                text += "(" + column.Length.Value + ")";
            }
            else if (column.Precision.HasValue)
            {
                text += "(" + column.Precision.Value + "," + column.Scale.GetValueOrDefault() + ")";
            }
            if (column.Nullable.GetValueOrDefault())
            {
                text += " NULL";
            }
            else
            {
                text += " NOT NULL";
            }
            if (column.Type == LogicalType.Decimal)
            {
                text += "，double 存储有精度损失";
            }
            return text;
        }

        private static string Comment(string text)
        {
            return (text ?? "").Replace("\r", " ").Replace("\n", " ");
        }
    }
}

using System.Globalization;
using System.Text;

namespace DbChronicle.CodeGen
{
    /// <summary>标识符命名转换：数据库的 snake_case / 保留字处理 → 各语言惯用写法。</summary>
    public static class CodeNaming
    {
        public static string ToPascalCase(string name)
        {
            var camel = ToCamelCase(name);
            if (string.IsNullOrEmpty(camel))
            {
                return camel;
            }
            return char.ToUpperInvariant(camel[0]) + camel.Substring(1);
        }

        public static string ToCamelCase(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "";
            }

            var parts = name.Split(new[] { '_', '-', '.', ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return "";
            }

            var sb = new StringBuilder();
            for (var i = 0; i < parts.Length; i++)
            {
                var part = IsAllUpper(parts[i]) ? parts[i].ToLowerInvariant() : parts[i];
                if (part.Length == 0)
                {
                    continue;
                }

                var head = i == 0 ? char.ToLowerInvariant(part[0]) : char.ToUpperInvariant(part[0]);
                sb.Append(head).Append(part.Substring(1));
            }

            return sb.ToString();
        }

        private static bool IsAllUpper(string text)
        {
            var hasLetter = false;
            foreach (var ch in text)
            {
                if (!char.IsLetter(ch))
                {
                    continue;
                }
                hasLetter = true;
                if (char.IsLower(ch))
                {
                    return false;
                }
            }
            return hasLetter;
        }

        /// <summary>Go 的导出字段名：保证首字母大写。</summary>
        public static string ToGoExported(string name)
        {
            var pascal = ToPascalCase(name);
            return pascal.Length == 0 ? "Field" : pascal;
        }

        /// <summary>C# / Java 的保留字避让。</summary>
        public static string EscapeIdentifier(string name, bool isCSharp)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }

            var keyword = IsKeyword(name.ToLowerInvariant());
            if (!keyword)
            {
                return name;
            }

            return isCSharp ? "@" + name : name + "_";
        }

        private static bool IsKeyword(string name)
        {
            switch (name)
            {
                case "abstract":
                case "as":
                case "base":
                case "bool":
                case "break":
                case "byte":
                case "case":
                case "catch":
                case "char":
                case "class":
                case "const":
                case "continue":
                case "decimal":
                case "default":
                case "delegate":
                case "do":
                case "double":
                case "else":
                case "enum":
                case "event":
                case "explicit":
                case "extern":
                case "false":
                case "finally":
                case "fixed":
                case "float":
                case "for":
                case "foreach":
                case "goto":
                case "if":
                case "implicit":
                case "in":
                case "int":
                case "interface":
                case "internal":
                case "is":
                case "lock":
                case "long":
                case "namespace":
                case "new":
                case "null":
                case "object":
                case "operator":
                case "out":
                case "override":
                case "params":
                case "private":
                case "protected":
                case "public":
                case "readonly":
                case "ref":
                case "return":
                case "sbyte":
                case "sealed":
                case "short":
                case "sizeof":
                case "stackalloc":
                case "static":
                case "string":
                case "struct":
                case "switch":
                case "this":
                case "throw":
                case "true":
                case "try":
                case "typeof":
                case "uint":
                case "ulong":
                case "unchecked":
                case "unsafe":
                case "ushort":
                case "using":
                case "virtual":
                case "void":
                case "volatile":
                case "while":
                    return true;
                default:
                    return false;
            }
        }

        public static string Invariant(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}

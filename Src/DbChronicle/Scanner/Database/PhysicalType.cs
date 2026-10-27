using System.Globalization;
using System.Text.RegularExpressions;

namespace DbChronicle.Scanner.Database
{
    /// <summary>
    /// 物理类型名的拆解结果：基础名 + 参数。
    /// 例：VARCHAR(64) → BaseName="varchar" Length=64；DECIMAL(18,2) → Precision=18 Scale=2。
    /// </summary>
    public sealed class PhysicalType
    {
        private static readonly Regex Pattern = new Regex(
            @"^\s*([A-Za-z_][A-Za-z0-9_ ]*?)\s*(?:\(\s*(\d+)\s*(?:,\s*(\d+)\s*)?\))?\s*$",
            RegexOptions.Compiled);

        public string BaseName { get; private set; } = "";

        public int? First { get; private set; }

        public int? Second { get; private set; }

        public static PhysicalType Parse(string declared)
        {
            var result = new PhysicalType();
            if (string.IsNullOrEmpty(declared))
            {
                return result;
            }

            var match = Pattern.Match(declared);
            if (!match.Success)
            {
                result.BaseName = declared.Trim().ToLowerInvariant();
                return result;
            }

            result.BaseName = match.Groups[1].Value.Trim().ToLowerInvariant();
            if (match.Groups[2].Success)
            {
                result.First = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            }
            if (match.Groups[3].Success)
            {
                result.Second = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            }
            return result;
        }

        public bool Is(string name)
        {
            return string.Equals(BaseName, name, System.StringComparison.OrdinalIgnoreCase);
        }

        public bool IsAny(params string[] names)
        {
            foreach (var name in names)
            {
                if (Is(name))
                {
                    return true;
                }
            }
            return false;
        }

        public bool Contains(string part)
        {
            return BaseName.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}

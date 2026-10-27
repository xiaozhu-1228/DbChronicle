using DbChronicle.Common.Enums;

namespace DbChronicle.Models
{
    public sealed class ScannedColumn
    {
        public string Name { get; set; } = "";

        public LogicalType Type { get; set; }

        public int? Length { get; set; }

        public int? Precision { get; set; }

        public int? Scale { get; set; }

        public bool Nullable { get; set; }

        public bool PrimaryKey { get; set; }

        public bool AutoIncrement { get; set; }

        public bool Unsigned { get; set; }

        public string Comment { get; set; }

        public string DefaultValue { get; set; }
    }
}
using DbChronicle.Common.Enums;

namespace DbChronicle.Models
{
    public sealed class ColumnDefinition
    {
        public string Name { get; set; } = "";
        public LogicalType? Type { get; set; }
        public int? Length { get; set; }
        public int? Precision { get; set; }
        public int? Scale { get; set; }
        public bool? Nullable { get; set; }
        public DefaultValueSpec DefaultValue { get; set; } = DefaultValueSpec.Undeclared;
        public string Comment { get; set; }
        public bool? PrimaryKey { get; set; }
        public bool? AutoIncrement { get; set; }
        public bool? Unique { get; set; }
        public bool? Unsigned { get; set; }
        public string OnUpdate { get; set; }
        public string Charset { get; set; }
        public string Collate { get; set; }
        public string ForeignKey { get; set; }

        public ColumnDefinition Clone()
        {
            return new ColumnDefinition
            {
                Name = Name,
                Type = Type,
                Length = Length,
                Precision = Precision,
                Scale = Scale,
                Nullable = Nullable,
                DefaultValue = DefaultValue,
                Comment = Comment,
                PrimaryKey = PrimaryKey,
                AutoIncrement = AutoIncrement,
                Unique = Unique,
                Unsigned = Unsigned,
                OnUpdate = OnUpdate,
                Charset = Charset,
                Collate = Collate,
                ForeignKey = ForeignKey
            };
        }
    }
}
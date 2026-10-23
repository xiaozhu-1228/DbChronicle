using System;

namespace DbChronicle.Demo.MySql
{
    /// <summary>
    /// information_schema.COLUMNS 的只读快照，用于生成逻辑模型基线。
    /// </summary>
    internal sealed class ColumnSnapshot
    {
        public string Name { get; set; }

        public string ColumnType { get; set; }

        public string DataType { get; set; }

        public int? Length { get; set; }

        public int? Precision { get; set; }

        public int? Scale { get; set; }

        public bool Nullable { get; set; }

        public string Default { get; set; }

        public bool HasDefault { get; set; }

        public string Comment { get; set; }

        public bool PrimaryKey { get; set; }

        public bool AutoIncrement { get; set; }

        public string Charset { get; set; }

        public string Collate { get; set; }

        public bool IsUnsigned
        {
            get
            {
                return ColumnType != null
                    && ColumnType.IndexOf("unsigned", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        public override string ToString()
        {
            return Name + "  " + ColumnType
                + "  null=" + (Nullable ? "YES" : "NO")
                + (HasDefault ? "  default=" + (Default ?? "NULL") : "")
                + (PrimaryKey ? "  PK" : "")
                + (AutoIncrement ? "  AUTO" : "")
                + (string.IsNullOrEmpty(Comment) ? "" : "  comment=" + Comment)
                + (string.IsNullOrEmpty(Charset) ? "" : "  charset=" + Charset)
                + (string.IsNullOrEmpty(Collate) ? "" : "  collate=" + Collate);
        }
    }
}

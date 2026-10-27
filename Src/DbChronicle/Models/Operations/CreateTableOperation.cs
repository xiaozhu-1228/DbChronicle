using System.Collections.Generic;

namespace DbChronicle.Models.Operations
{
    public sealed class CreateTableOperation : SchemaOperation
    {
        public string Comment { get; set; }
        public List<ColumnDefinition> Columns { get; } = new List<ColumnDefinition>();
        public List<string> PrimaryKeyColumns { get; } = new List<string>();
        public string PrimaryKeyName { get; set; }

        /// <summary>建表级 &lt;foreignKey&gt; 子节点（SQLite 等只能在建表时声明外键的方言内联使用）。</summary>
        public List<ForeignKeyOperation> ForeignKeys { get; } = new List<ForeignKeyOperation>();
    }
}
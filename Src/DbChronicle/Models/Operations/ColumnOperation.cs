using DbChronicle.Common.Enums;

namespace DbChronicle.Models.Operations
{
    public sealed class ColumnOperation : SchemaOperation
    {
        public ColumnAction Action { get; set; }
        public ColumnDefinition Definition { get; set; } = new ColumnDefinition();
        public ColumnDefinition PreviousDefinition { get; set; }
        public string RenamedFrom { get; set; }
        public string After { get; set; }
        public string Comment { get; set; }

        public string Name
        {
            get { return Definition.Name; }
            set { Definition.Name = value; }
        }
    }
}
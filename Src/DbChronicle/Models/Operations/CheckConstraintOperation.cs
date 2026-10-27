using DbChronicle.Common.Enums;

namespace DbChronicle.Models.Operations
{
    public sealed class CheckConstraintOperation : SchemaOperation
    {
        public ObjectAction Action { get; set; }
        public string Name { get; set; } = "";
        public string Expression { get; set; }
    }
}
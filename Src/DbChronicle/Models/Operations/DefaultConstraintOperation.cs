using DbChronicle.Common.Enums;

namespace DbChronicle.Models.Operations
{
    public sealed class DefaultConstraintOperation : SchemaOperation
    {
        public ObjectAction Action { get; set; }
        public string Name { get; set; } = "";
        public string Column { get; set; }
        public string Value { get; set; }
    }
}
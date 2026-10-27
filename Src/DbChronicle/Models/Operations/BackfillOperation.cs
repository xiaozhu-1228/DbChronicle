namespace DbChronicle.Models.Operations
{
    public sealed class BackfillOperation : SchemaOperation
    {
        public string Column { get; set; }

        public string Value { get; set; }

        public string Where { get; set; }
}
}
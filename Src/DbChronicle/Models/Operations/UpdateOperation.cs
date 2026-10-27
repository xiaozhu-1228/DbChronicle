namespace DbChronicle.Models.Operations
{
    public sealed class UpdateOperation : SchemaOperation
    {
        public string Set { get; set; }

        public string Where { get; set; }
    }
}
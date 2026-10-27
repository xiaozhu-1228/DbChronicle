namespace DbChronicle.Models.Operations
{
    public sealed class DeleteOperation : SchemaOperation
    {
        public string Where { get; set; }
    }
}
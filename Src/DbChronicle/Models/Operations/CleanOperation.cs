namespace DbChronicle.Models.Operations
{
    public sealed class CleanOperation : SchemaOperation
    {
        public string Rules { get; set; }

        public string Where { get; set; }
    }
}
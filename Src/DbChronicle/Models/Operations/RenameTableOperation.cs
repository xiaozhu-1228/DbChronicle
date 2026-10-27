namespace DbChronicle.Models.Operations
{
    public sealed class RenameTableOperation : SchemaOperation
    {
        public string NewName { get; set; } = "";
    }
}
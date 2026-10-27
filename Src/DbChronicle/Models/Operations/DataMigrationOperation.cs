using System.Collections.Generic;

namespace DbChronicle.Models.Operations
{
    public sealed class DataMigrationOperation : SchemaOperation
    {
        public Dictionary<string, string> SqlByDialect { get; } = new Dictionary<string, string>();
    }
}

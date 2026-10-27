using System.Collections.Generic;

namespace DbChronicle.Models.Operations
{
    public sealed class RawSqlOperation : SchemaOperation
    {
        public Dictionary<string, string> SqlByDialect { get; } = new Dictionary<string, string>();
    }
}

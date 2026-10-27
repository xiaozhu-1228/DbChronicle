using DbChronicle.Execution;
using DbChronicle.Models;

namespace DbChronicle.Scanner.Database
{
    /// <summary>各方言的数据库元数据读取器（只读查询，不修改目标库）。</summary>
    public interface IDatabaseMetadataReader
    {
        string DialectName { get; }

        ScanResult Read(IDbExecutor executor, ScanDatabaseOptions options);
    }
}

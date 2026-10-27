using System;

using DbChronicle.Execution;
using DbChronicle.Models;

namespace DbChronicle.Scanner.Database
{
    /// <summary>
    /// 数据库反向扫描（`scan-db`）：读取目标库现有结构，生成可直接冻结的基线变更单元。
    /// 只做只读元数据查询，不修改目标库。
    /// </summary>
    public sealed class DatabaseScanner
    {
        private readonly IDbExecutor _executor;
        private readonly BaselineXmlWriter _xmlWriter = new BaselineXmlWriter();

        public DatabaseScanner(IDbExecutor executor)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        public ScanResult Scan(ScanDatabaseOptions options = null)
        {
            var reader = CreateReader(_executor.DialectName);
            if (reader == null)
            {
                throw new NotSupportedException("暂不支持对 " + _executor.DialectName + " 做数据库反向扫描");
            }

            return reader.Read(_executor, options ?? new ScanDatabaseOptions());
        }

        public string GenerateXml(ScanResult result, string author, string date)
        {
            return _xmlWriter.Generate(result, author, date, "数据库结构");
        }

        public static IDatabaseMetadataReader CreateReader(string dialectName)
        {
            switch ((dialectName ?? "").Trim().ToLowerInvariant())
            {
                case "mysql":
                    return new MySqlMetadataReader();
                case "mariadb":
                    return new MariaDbMetadataReader();
                case "postgresql":
                    return new PostgreSqlMetadataReader();
                case "sqlserver":
                    return new SqlServerMetadataReader();
                case "sqlite":
                    return new SqliteMetadataReader();
                default:
                    return null;
            }
        }
    }
}

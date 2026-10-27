namespace DbChronicle.Scanner.Database
{
    /// <summary>MariaDB 元数据读取：与 MySQL 共用 information_schema 查询。</summary>
    public sealed class MariaDbMetadataReader : MySqlMetadataReader
    {
        public override string DialectName => "mariadb";
    }
}

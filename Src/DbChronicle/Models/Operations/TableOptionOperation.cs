namespace DbChronicle.Models.Operations
{
    /// <summary>
    /// 表选项（存储引擎 / 字符集 / 排序规则 / 表空间）。
    /// MySQL / MariaDB 支持 ENGINE / CHARSET / COLLATE，PostgreSQL 支持 TABLESPACE，
    /// SQL Server 与 SQLite 无对应语法，编译时报错。
    /// </summary>
    public sealed class TableOptionOperation : SchemaOperation
    {
        public string Engine { get; set; }

        public string Charset { get; set; }

        public string Collate { get; set; }

        public string Tablespace { get; set; }
    }
}

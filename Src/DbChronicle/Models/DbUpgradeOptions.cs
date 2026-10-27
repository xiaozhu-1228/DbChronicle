using DbChronicle.Execution;

namespace DbChronicle.Models
{
    public sealed class DbUpgradeOptions
    {
        /// <summary>只生成并打印 SQL，不执行。</summary>
        public bool DryRun { get; set; }

        /// <summary>记录到版本表的执行人；默认当前用户。</summary>
        public string AppliedBy { get; set; }

        /// <summary>可选：仅用于获取库级互斥锁；为空时不加锁。</summary>
        public string ConnectionString { get; set; }

        /// <summary>可选：库级锁工厂；与 ConnectionString 同时提供才生效。</summary>
        public IDbLockFactory LockFactory { get; set; }

        /// <summary>锁等待秒数；0 表示使用配置文件中的 lockTimeoutSeconds。</summary>
        public int LockTimeoutSeconds { get; set; }

        /// <summary>清空该单元已记录的语句进度后重跑（等价 CLI --restart）。</summary>
        public bool Restart { get; set; }

        /// <summary>把指定单元标记为已应用而不执行（等价 CLI --mark-applied）。</summary>
        public string MarkAppliedChangeId { get; set; }

        /// <summary>
        /// 失败后继续执行其余表 / 单元。
        /// 默认 null：无事务 DDL 的方言（MySQL / MariaDB）继续，事务方言遇错即停。
        /// </summary>
        public bool? ContinueOnError { get; set; }
    }
}
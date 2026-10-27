using System;

using DbChronicle.Configuration;
using DbChronicle.Execution;

namespace DbChronicle.Models
{
    public sealed class MigratorOptions
    {
        public string ConfigPath { get; set; }

        public DbChronicleConfig Config { get; set; }

        public string ConnectionString { get; set; }

        public string AppliedBy { get; set; }

        public bool DryRun { get; set; }

        public bool Restart { get; set; }

        public string MarkAppliedChangeId { get; set; }

        public int LockTimeoutSeconds { get; set; }

        public IDbExecutorFactory ExecutorFactory { get; set; }

        public IDbLockFactory LockFactory { get; set; }

        public Action<string> Log { get; set; }
}
}
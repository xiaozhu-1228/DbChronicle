using System;

namespace DbChronicle.Models
{
    public sealed class ApplyOptions
    {
        public string ConnectionString { get; set; }

        public string AppliedBy { get; set; }

        public bool DryRun { get; set; }

        public string MarkAppliedChangeId { get; set; }

        public bool Restart { get; set; }

        public int LockTimeoutSeconds { get; set; } = 10;

        /// <summary>
        /// 失败后是否继续执行其余表 / 单元。
        /// null（默认）= 按方言决定：无事务 DDL 的方言（MySQL / MariaDB）继续，事务方言遇错即停。
        /// </summary>
        public bool? ContinueOnError { get; set; }

        public Action<string> Log { get; set; }

        public void WriteLog(string message)
        {
            var handler = Log;
            if (handler != null)
            {
                handler(message);
            }
        }
    }
}
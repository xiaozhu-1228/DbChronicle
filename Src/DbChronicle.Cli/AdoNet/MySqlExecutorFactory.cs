using System;
using DbChronicle.Abstractions.Execution;
using DbChronicle.Runtime.Execution;
using MySqlConnector;

namespace DbChronicle.Executor.AdoNet
{
    public sealed class MySqlExecutorFactory : IDbExecutorFactory, IDbLockFactory
    {
        public string DialectName
        {
            get { return "mysql"; }
        }

        public IDbExecutor Create(string connectionString)
        {
            return new MySqlExecutor(connectionString);
        }

        IDbLock IDbLockFactory.Create(string connectionString)
        {
            var builder = new MySqlConnectionStringBuilder(connectionString);
            return new MySqlLock(connectionString, builder.Database);
        }

        public static IDbExecutorFactory Resolve(string dialectName)
        {
            var name = (dialectName ?? "").Trim().ToLowerInvariant();
            if (name == "mysql" || name == "mariadb")
            {
                return new MySqlExecutorFactory();
            }
            throw new NotSupportedException("一期暂未提供该方言的执行器：" + dialectName);
        }

        public static IDbLockFactory ResolveLock(string dialectName)
        {
            var name = (dialectName ?? "").Trim().ToLowerInvariant();
            if (name == "mysql" || name == "mariadb")
            {
                return new MySqlExecutorFactory();
            }
            return null;
        }
    }
}

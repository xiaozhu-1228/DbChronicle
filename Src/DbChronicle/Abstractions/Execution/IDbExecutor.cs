using System;
using System.Collections.Generic;

namespace DbChronicle.Abstractions.Execution
{
    public interface IDbExecutor : IDisposable
    {
        string DialectName { get; }

        bool SupportsTransactionalDdl { get; }

        void Execute(string sql);

        object Scalar(string sql);

        List<Dictionary<string, object>> Query(string sql);

        void BeginTransaction();

        void Commit();

        void Rollback();
    }

    public interface IDbExecutorFactory
    {
        string DialectName { get; }

        IDbExecutor Create(string connectionString);
    }

    public interface IDbLock : IDisposable
    {
        bool TryAcquire(TimeSpan timeout);
    }

    public interface IDbLockFactory
    {
        IDbLock Create(string connectionString);
    }
}

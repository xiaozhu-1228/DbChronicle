using System;
using System.Collections.Generic;

using DbChronicle.Abstractions.Diagnostics;
using DbChronicle.Abstractions.Execution;
using DbChronicle.Abstractions.Versioning;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Dialects;
using DbChronicle.Compilation.Splitting;
using DbChronicle.Configuration;
using DbChronicle.Domain.Model;
using DbChronicle.Domain.Operations;
using DbChronicle.Domain.Sql;
using DbChronicle.Domain.Structures;
using DbChronicle.Runtime.Execution;
using DbChronicle.Runtime.Versioning;
using DbChronicle.Runtime.Rollback;
using DbChronicle.Runtime.Naming;
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

using System.Collections.Generic;
using System;

namespace DbChronicle.Execution
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
}
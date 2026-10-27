using System.Collections.Generic;

using DbChronicle.Execution;

namespace DbChronicle.IntegrationTests
{
    internal sealed class NoTransactionExecutor : IDbExecutor
    {
        private readonly IDbExecutor _inner;

        public NoTransactionExecutor(IDbExecutor inner)
        {
            _inner = inner;
        }

        public string DialectName
        {
            get { return _inner.DialectName; }
        }

        public bool SupportsTransactionalDdl
        {
            get { return false; }
        }

        public void Execute(string sql)
        {
            _inner.Execute(sql);
        }

        public object Scalar(string sql)
        {
            return _inner.Scalar(sql);
        }

        public List<Dictionary<string, object>> Query(string sql)
        {
            return _inner.Query(sql);
        }

        public void BeginTransaction()
        {
            _inner.BeginTransaction();
        }

        public void Commit()
        {
            _inner.Commit();
        }

        public void Rollback()
        {
            _inner.Rollback();
        }

        public void Dispose()
        {
            _inner.Dispose();
        }
    }
}
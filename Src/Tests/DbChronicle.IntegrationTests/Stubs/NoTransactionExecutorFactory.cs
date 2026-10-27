using DbChronicle.Execution;

namespace DbChronicle.IntegrationTests
{
    internal sealed class NoTransactionExecutorFactory : IDbExecutorFactory
    {
        private readonly IDbExecutorFactory _inner;

        public NoTransactionExecutorFactory(IDbExecutorFactory inner)
        {
            _inner = inner;
        }

        public string DialectName
        {
            get { return _inner.DialectName; }
        }

        public IDbExecutor Create(string connectionString)
        {
            return new NoTransactionExecutor(_inner.Create(connectionString));
        }
    }
}
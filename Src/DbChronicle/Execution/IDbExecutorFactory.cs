namespace DbChronicle.Execution
{
    public interface IDbExecutorFactory
    {
        string DialectName { get; }

        IDbExecutor Create(string connectionString);
    }
}
namespace DbChronicle.Execution
{
    public interface IDbLockFactory
    {
        IDbLock Create(string connectionString);
}
}
using Npgsql;

namespace DbChronicle.Executor.AdoNet
{
    public sealed class PostgreSqlExecutor : AdoNetExecutor
    {
        public PostgreSqlExecutor(string connectionString)
            : base(new NpgsqlConnection(connectionString), "postgresql", true)
        {
        }
    }
}

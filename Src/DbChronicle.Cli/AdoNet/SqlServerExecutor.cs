using Microsoft.Data.SqlClient;

namespace DbChronicle.Executor.AdoNet
{
    public sealed class SqlServerExecutor : AdoNetExecutor
    {
        public SqlServerExecutor(string connectionString)
            : base(new SqlConnection(connectionString), "sqlserver", true)
        {
        }
    }
}

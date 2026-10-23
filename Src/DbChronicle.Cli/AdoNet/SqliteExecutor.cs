using Microsoft.Data.Sqlite;

namespace DbChronicle.Executor.AdoNet
{
    public sealed class SqliteExecutor : AdoNetExecutor
    {
        public SqliteExecutor(string connectionString)
            : base(new SqliteConnection(connectionString), "sqlite", true)
        {
        }

        protected override void OnOpened()
        {
            using (var command = Connection.CreateCommand())
            {
                command.CommandText = "PRAGMA foreign_keys = ON;";
                command.ExecuteNonQuery();
            }
        }
    }
}

using System;

using DbChronicle.Execution;
using Microsoft.Data.SqlClient;

namespace DbChronicle.Executor.AdoNet
{
    public sealed class SqlServerLock : IDbLock
    {
        private readonly SqlConnection _connection;
        private readonly string _resource;
        private bool _acquired;

        public SqlServerLock(string connectionString, string databaseName)
        {
            _connection = new SqlConnection(connectionString);
            _connection.Open();
            _resource = "dbchronicle:" + (string.IsNullOrEmpty(databaseName) ? "default" : databaseName);
        }

        public bool TryAcquire(TimeSpan timeout)
        {
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "sp_getapplock";
                command.CommandType = System.Data.CommandType.StoredProcedure;
                command.Parameters.AddWithValue("@Resource", _resource);
                command.Parameters.AddWithValue("@LockMode", "Exclusive");
                command.Parameters.AddWithValue("@LockOwner", "Session");
                command.Parameters.AddWithValue("@LockTimeout", (int)Math.Max(0, timeout.TotalMilliseconds));

                var result = command.ExecuteScalar();
                var code = result == null || result == DBNull.Value ? -1 : Convert.ToInt32(result);
                _acquired = code >= 0;
                return _acquired;
            }
        }

        public void Dispose()
        {
            if (_acquired)
            {
                try
                {
                    using (var command = _connection.CreateCommand())
                    {
                        command.CommandText = "sp_releaseapplock";
                        command.CommandType = System.Data.CommandType.StoredProcedure;
                        command.Parameters.AddWithValue("@Resource", _resource);
                        command.Parameters.AddWithValue("@LockOwner", "Session");
                        command.ExecuteNonQuery();
                    }
                }
                catch
                {
                }
                _acquired = false;
            }
            _connection.Dispose();
        }
    }
}
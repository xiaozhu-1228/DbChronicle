using System;
using System.Globalization;
using DbChronicle.Abstractions.Execution;
using MySqlConnector;

namespace DbChronicle.Executor.AdoNet
{
    public sealed class MySqlLock : IDbLock
    {
        private readonly MySqlConnection _connection;
        private readonly string _lockName;
        private bool _acquired;

        public MySqlLock(string connectionString, string databaseName)
        {
            _connection = new MySqlConnection(connectionString);
            _connection.Open();
            _lockName = "dbchronicle:" + (string.IsNullOrEmpty(databaseName) ? "default" : databaseName);
        }

        public bool TryAcquire(TimeSpan timeout)
        {
            var seconds = (int)Math.Max(0, timeout.TotalSeconds);
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT GET_LOCK(@name, @timeout)";
                command.Parameters.AddWithValue("@name", _lockName);
                command.Parameters.AddWithValue("@timeout", seconds);
                var value = command.ExecuteScalar();
                _acquired = value != null && value != DBNull.Value && Convert.ToInt32(value, CultureInfo.InvariantCulture) == 1;
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
                        command.CommandText = "SELECT RELEASE_LOCK(@name)";
                        command.Parameters.AddWithValue("@name", _lockName);
                        command.ExecuteScalar();
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

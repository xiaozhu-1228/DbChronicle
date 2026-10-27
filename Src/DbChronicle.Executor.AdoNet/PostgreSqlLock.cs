using System.Threading;
using System;

using DbChronicle.Execution;
using Npgsql;

namespace DbChronicle.Executor.AdoNet
{
    public sealed class PostgreSqlLock : IDbLock
    {
        private readonly NpgsqlConnection _connection;
        private readonly string _key;
        private bool _acquired;

        public PostgreSqlLock(string connectionString, string databaseName)
        {
            _connection = new NpgsqlConnection(connectionString);
            _connection.Open();
            _key = "dbchronicle:" + (string.IsNullOrEmpty(databaseName) ? "default" : databaseName);
        }

        public bool TryAcquire(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow.Add(timeout);
            while (true)
            {
                using (var command = _connection.CreateCommand())
                {
                    command.CommandText = "SELECT pg_try_advisory_lock(hashtext(@key))";
                    command.Parameters.AddWithValue("key", _key);
                    var value = command.ExecuteScalar();
                    if (value != null && value != DBNull.Value && Convert.ToBoolean(value))
                    {
                        _acquired = true;
                        return true;
                    }
                }

                if (DateTime.UtcNow >= deadline)
                {
                    return false;
                }
                Thread.Sleep(200);
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
                        command.CommandText = "SELECT pg_advisory_unlock(hashtext(@key))";
                        command.Parameters.AddWithValue("key", _key);
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
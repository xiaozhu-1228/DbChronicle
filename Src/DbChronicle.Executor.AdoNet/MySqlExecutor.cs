using System;
using System.Collections.Generic;

using DbChronicle.Execution;
using MySqlConnector;

namespace DbChronicle.Executor.AdoNet
{
    public sealed class MySqlExecutor : IDbExecutor
    {
        private readonly MySqlConnection _connection;
        private MySqlTransaction _transaction;

        public MySqlExecutor(string connectionString)
        {
            _connection = new MySqlConnection(connectionString);
            _connection.Open();
        }

        public string DialectName
        {
            get { return "mysql"; }
        }

        public bool SupportsTransactionalDdl
        {
            get { return false; }
        }

        public void Execute(string sql)
        {
            using (var command = CreateCommand(sql))
            {
                command.ExecuteNonQuery();
            }
        }

        public object Scalar(string sql)
        {
            using (var command = CreateCommand(sql))
            {
                var value = command.ExecuteScalar();
                return value == DBNull.Value ? null : value;
            }
        }

        public List<Dictionary<string, object>> Query(string sql)
        {
            var result = new List<Dictionary<string, object>>();
            using (var command = CreateCommand(sql))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        var value = reader.GetValue(i);
                        row[reader.GetName(i)] = value == DBNull.Value ? null : value;
                    }
                    result.Add(row);
                }
            }
            return result;
        }

        public void BeginTransaction()
        {
            _transaction = _connection.BeginTransaction();
        }

        public void Commit()
        {
            if (_transaction != null)
            {
                _transaction.Commit();
                _transaction.Dispose();
                _transaction = null;
            }
        }

        public void Rollback()
        {
            if (_transaction != null)
            {
                _transaction.Rollback();
                _transaction.Dispose();
                _transaction = null;
            }
        }

        public void Dispose()
        {
            if (_transaction != null)
            {
                _transaction.Dispose();
                _transaction = null;
            }
            _connection.Dispose();
        }

        private MySqlCommand CreateCommand(string sql)
        {
            var command = _connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 600;
            if (_transaction != null)
            {
                command.Transaction = _transaction;
            }
            return command;
        }
    }
}
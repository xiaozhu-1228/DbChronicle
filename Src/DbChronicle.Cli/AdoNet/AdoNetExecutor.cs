using System;
using System.Collections.Generic;
using System.Data.Common;
using DbChronicle.Abstractions.Execution;

namespace DbChronicle.Executor.AdoNet
{
    public abstract class AdoNetExecutor : IDbExecutor
    {
        private readonly DbConnection _connection;
        private readonly string _dialectName;
        private readonly bool _transactionalDdl;
        private DbTransaction _transaction;

        protected AdoNetExecutor(DbConnection connection, string dialectName, bool transactionalDdl)
        {
            _connection = connection;
            _dialectName = dialectName;
            _transactionalDdl = transactionalDdl;
            _connection.Open();
            OnOpened();
        }

        public string DialectName
        {
            get { return _dialectName; }
        }

        public bool SupportsTransactionalDdl
        {
            get { return _transactionalDdl; }
        }

        protected DbConnection Connection
        {
            get { return _connection; }
        }

        protected virtual void OnOpened()
        {
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
            if (_transaction == null)
            {
                return;
            }
            _transaction.Commit();
            _transaction.Dispose();
            _transaction = null;
        }

        public void Rollback()
        {
            if (_transaction == null)
            {
                return;
            }
            try
            {
                _transaction.Rollback();
            }
            finally
            {
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

        protected DbCommand CreateCommand(string sql)
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

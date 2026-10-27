using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;

using DbChronicle.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DbChronicle.Executor.EfCore
{
    /// <summary>
    /// 基于宿主 <see cref="DbContext"/> 的执行器：复用上下文已有的连接与事务，
    /// 适合"应用内嵌、不想再开一条连接"的场景。DDL 与 DML 都走同一连接。
    /// </summary>
    public sealed class EfCoreExecutor : IDbExecutor
    {
        private readonly DbContext _context;
        private readonly DbConnection _connection;
        private IDbContextTransaction _transaction;
        private bool _openedHere;
        private bool _disposed;

        public EfCoreExecutor(DbContext context, string dialectName)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            DialectName = (dialectName ?? "").Trim().ToLowerInvariant();
            _connection = context.Database.GetDbConnection();
        }

        public string DialectName { get; }

        /// <summary>MySQL / MariaDB 的 DDL 隐式提交，不能整单元回滚。</summary>
        public bool SupportsTransactionalDdl =>
            DialectName != "mysql" && DialectName != "mariadb";

        public void Execute(string sql)
        {
            EnsureOpen();
            using (var command = CreateCommand(sql))
            {
                command.ExecuteNonQuery();
            }
        }

        public object Scalar(string sql)
        {
            EnsureOpen();
            using (var command = CreateCommand(sql))
            {
                var value = command.ExecuteScalar();
                return value is DBNull ? null : value;
            }
        }

        public List<Dictionary<string, object>> Query(string sql)
        {
            EnsureOpen();
            var rows = new List<Dictionary<string, object>>();
            using (var command = CreateCommand(sql))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        row[reader.GetName(i)] = value;
                    }
                    rows.Add(row);
                }
            }
            return rows;
        }

        public void BeginTransaction()
        {
            EnsureOpen();
            if (_transaction != null)
            {
                return;
            }
            _transaction = _context.Database.BeginTransaction();
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
            _transaction.Rollback();
            _transaction.Dispose();
            _transaction = null;
        }

        /// <summary>宿主已开启的事务：让执行器复用，避免嵌套事务异常。</summary>
        public void UseExistingTransaction()
        {
            _transaction = _context.Database.CurrentTransaction;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            if (_transaction != null)
            {
                _transaction.Dispose();
                _transaction = null;
            }
            if (_openedHere && _connection.State == ConnectionState.Open)
            {
                _connection.Close();
            }
        }

        private void EnsureOpen()
        {
            if (_connection.State == ConnectionState.Open)
            {
                return;
            }
            _connection.Open();
            _openedHere = true;
        }

        private DbCommand CreateCommand(string sql)
        {
            var command = _connection.CreateCommand();
            command.CommandText = sql;
            if (_transaction != null)
            {
                command.Transaction = _transaction.GetDbTransaction();
            }
            return command;
        }
    }
}

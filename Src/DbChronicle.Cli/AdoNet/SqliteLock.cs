using System;
using System.IO;
using System.Threading;
using DbChronicle.Abstractions.Execution;
using DbChronicle.Runtime.Execution;
using Microsoft.Data.Sqlite;

namespace DbChronicle.Executor.AdoNet
{
    public sealed class SqliteLock : IDbLock
    {
        private readonly string _lockPath;
        private FileStream _stream;

        public SqliteLock(string connectionString)
        {
            var builder = new SqliteConnectionStringBuilder(connectionString);
            var dataSource = string.IsNullOrEmpty(builder.DataSource) ? "dbchronicle.db" : builder.DataSource;
            var fullPath = Path.GetFullPath(dataSource);
            _lockPath = fullPath + ".dbchronicle.lock";
        }

        public bool TryAcquire(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow.Add(timeout);
            var directory = Path.GetDirectoryName(_lockPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            while (true)
            {
                try
                {
                    _stream = new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    return true;
                }
                catch (IOException)
                {
                    if (DateTime.UtcNow >= deadline)
                    {
                        return false;
                    }
                    Thread.Sleep(200);
                }
            }
        }

        public void Dispose()
        {
            if (_stream != null)
            {
                _stream.Dispose();
                _stream = null;
            }
        }
    }
}

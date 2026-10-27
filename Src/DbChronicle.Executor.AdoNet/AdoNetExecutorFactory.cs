using System;

using DbChronicle.Execution;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;

namespace DbChronicle.Executor.AdoNet
{
    public sealed class AdoNetExecutorFactory : IDbExecutorFactory, IDbLockFactory
    {
        private readonly string _dialect;

        public AdoNetExecutorFactory(string dialect)
        {
            _dialect = Normalize(dialect);
        }

        public string DialectName
        {
            get { return _dialect; }
        }

        public static AdoNetExecutorFactory Resolve(string dialect)
        {
            return new AdoNetExecutorFactory(dialect);
        }

        public static bool IsSupported(string dialect)
        {
            var name = Normalize(dialect);
            return name == "mysql" || name == "mariadb" || name == "postgresql" || name == "sqlserver" || name == "sqlite";
        }

        public IDbExecutor Create(string connectionString)
        {
            switch (_dialect)
            {
                case "mysql":
                case "mariadb":
                    return new MySqlExecutor(connectionString);

                case "postgresql":
                    return new PostgreSqlExecutor(connectionString);

                case "sqlserver":
                    return new SqlServerExecutor(connectionString);

                case "sqlite":
                    return new SqliteExecutor(connectionString);

                default:
                    throw new NotSupportedException("暂未提供该方言的执行器：" + _dialect);
            }
        }

        IDbLock IDbLockFactory.Create(string connectionString)
        {
            switch (_dialect)
            {
                case "mysql":
                case "mariadb":
                    return new MySqlLock(connectionString, new MySqlConnectionStringBuilder(connectionString).Database);

                case "postgresql":
                    return new PostgreSqlLock(connectionString, new NpgsqlConnectionStringBuilder(connectionString).Database);

                case "sqlserver":
                    return new SqlServerLock(connectionString, new SqlConnectionStringBuilder(connectionString).InitialCatalog);

                case "sqlite":
                    return new SqliteLock(connectionString);

                default:
                    return null;
            }
        }

        private static string Normalize(string dialect)
        {
            return (dialect ?? "").Trim().ToLowerInvariant();
        }
    }
}
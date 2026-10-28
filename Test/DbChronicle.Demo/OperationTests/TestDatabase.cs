using System.IO;
using System;

using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;

namespace DbChronicle.Demo.OperationTests
{
    internal static class TestDatabase
    {
        public static string DatabaseName { get; private set; }

        public static bool UsedFallback { get; private set; }

        public static string Prepare(string dialect, string baseConnectionString, string databaseName, string sqlitePath)
        {
            DatabaseName = databaseName;
            UsedFallback = false;

            switch (dialect)
            {
                case "mysql":
                    return PrepareMySql(baseConnectionString, databaseName);

                case "postgresql":
                    return PreparePostgreSql(baseConnectionString, databaseName);

                case "sqlite":
                    Directory.CreateDirectory(Path.GetDirectoryName(sqlitePath) ?? ".");
                    return "Data Source=" + sqlitePath;

                default:
                    throw new NotSupportedException("操作测试暂不支持方言：" + dialect);
            }
        }

        public static string ReadVersion(string dialect, DbChronicle.Execution.IDbExecutor executor)
        {
            switch (dialect)
            {
                case "mysql":
                    return Convert.ToString(executor.Scalar("SELECT VERSION()"));

                case "postgresql":
                    return Convert.ToString(executor.Scalar("SHOW server_version"));

                case "sqlite":
                    return Convert.ToString(executor.Scalar("SELECT sqlite_version()"));

                default:
                    return "";
            }
        }

        private static string PrepareMySql(string connectionString, string databaseName)
        {
            var target = new MySqlConnectionStringBuilder(connectionString);
            target.Database = databaseName;
            target.AllowUserVariables = true;

            try
            {
                var admin = new MySqlConnectionStringBuilder(connectionString);
                admin.Database = "";
                using (var connection = new MySqlConnection(admin.ConnectionString))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "CREATE DATABASE IF NOT EXISTS `" + databaseName.Replace("`", "``")
                            + "` CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci";
                        command.ExecuteNonQuery();
                    }
                }

                return target.ConnectionString;
            }
            catch (Exception ex)
            {
                UsedFallback = true;
                Console.WriteLine("警告：专用测试库创建失败（" + ex.Message + "），回退到原连接串。"
                    + "测试对象均以 dcops_ 前缀命名并在每个用例前清理。");
                return connectionString;
            }
        }

        private static string PreparePostgreSql(string connectionString, string databaseName)
        {
            var target = new NpgsqlConnectionStringBuilder(connectionString);
            target.Database = databaseName;
            target.IncludeErrorDetail = true;

            try
            {
                var admin = new NpgsqlConnectionStringBuilder(connectionString);
                admin.Database = "postgres";
                using (var connection = new NpgsqlConnection(admin.ConnectionString))
                {
                    connection.Open();
                    var exists = false;
                    using (var query = connection.CreateCommand())
                    {
                        query.CommandText = "SELECT 1 FROM pg_database WHERE datname = @name";
                        query.Parameters.AddWithValue("@name", databaseName);
                        exists = query.ExecuteScalar() != null;
                    }

                    if (!exists)
                    {
                        using (var create = connection.CreateCommand())
                        {
                            create.CommandText = "CREATE DATABASE \"" + databaseName.Replace("\"", "\"\"") + "\"";
                            create.ExecuteNonQuery();
                        }
                    }
                }

                return target.ConnectionString;
            }
            catch (Exception ex)
            {
                UsedFallback = true;
                Console.WriteLine("警告：专用测试库创建失败（" + ex.Message + "），回退到原连接串。"
                    + "测试对象均以 dcops_ 前缀命名并在每个用例前清理。");
                return connectionString;
            }
        }

        public static string ResolveSqlitePath(string caseDirectory)
        {
            Directory.CreateDirectory(caseDirectory);
            return Path.Combine(caseDirectory, "ops.db");
        }
    }
}

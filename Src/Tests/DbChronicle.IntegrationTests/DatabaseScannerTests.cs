using System.Linq;

using DbChronicle.Executor.AdoNet;
using DbChronicle.Models;
using DbChronicle.Scanner.Database;
using Xunit;

namespace DbChronicle.IntegrationTests
{
    /// <summary>数据库反向扫描（scan-db）：对真实 SQLite 库读取结构并生成可用的基线草案。</summary>
    public class DatabaseScannerTests
    {
        private static string CreateDatabase(string dbPath)
        {
            var connectionString = "Data Source=" + dbPath;
            using (var executor = new SqliteExecutor(connectionString))
            {
                executor.Execute(
                    "CREATE TABLE \"nor_user\" (\n"
                    + "  \"id\" INTEGER PRIMARY KEY AUTOINCREMENT,\n"
                    + "  \"name\" VARCHAR(64) NOT NULL,\n"
                    + "  \"balance\" NUMERIC(18,2) NULL DEFAULT 0,\n"
                    + "  \"active\" INTEGER NOT NULL DEFAULT 1,\n"
                    + "  \"created_at\" TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,\n"
                    + "  CONSTRAINT \"ck_balance\" CHECK (\"balance\" >= 0)\n"
                    + ");");
                executor.Execute("CREATE UNIQUE INDEX \"uk_user_name\" ON \"nor_user\" (\"name\");");
                executor.Execute("CREATE INDEX \"idx_user_balance\" ON \"nor_user\" (\"balance\");");
                executor.Execute(
                    "CREATE TABLE \"nor_order\" (\n"
                    + "  \"id\" INTEGER PRIMARY KEY AUTOINCREMENT,\n"
                    + "  \"user_id\" INTEGER NOT NULL,\n"
                    + "  \"order_no\" VARCHAR(32) NOT NULL,\n"
                    + "  CONSTRAINT \"fk_order_user\" FOREIGN KEY (\"user_id\") REFERENCES \"nor_user\" (\"id\") ON DELETE CASCADE\n"
                    + ");");
            }
            return connectionString;
        }

        [Fact]
        public void Scan_ShouldReadTablesColumnsIndexesAndForeignKeys()
        {
            var dbPath = TestSupport.TempFile("scan", ".db");
            var connectionString = CreateDatabase(dbPath);

            using var executor = new SqliteExecutor(connectionString);
            var scanner = new DatabaseScanner(executor);
            var result = scanner.Scan();

            Assert.Equal(2, result.Tables.Count);

            var user = result.Tables.Single(x => x.Name == "nor_user");
            Assert.Equal(5, user.Columns.Count);
            Assert.Equal(64, user.Columns.Single(x => x.Name == "name").Length);
            Assert.False(user.Columns.Single(x => x.Name == "name").Nullable);
            Assert.True(user.Columns.Single(x => x.Name == "id").AutoIncrement);
            Assert.Equal(2, user.Columns.Single(x => x.Name == "balance").Scale);
            Assert.Equal(18, user.Columns.Single(x => x.Name == "balance").Precision);
            Assert.Contains(user.Indexes, x => x.Name == "idx_user_balance");
            Assert.Contains(user.Indexes, x => x.Name == "uk_user_name" && x.Unique);

            var order = result.Tables.Single(x => x.Name == "nor_order");
            Assert.Single(order.ForeignKeys);
            Assert.Equal("nor_user", order.ForeignKeys[0].RefTable);
            Assert.Equal("user_id", order.ForeignKeys[0].Columns[0]);
            Assert.Equal("CASCADE", order.ForeignKeys[0].OnDelete);
        }

        [Fact]
        public void Scan_ShouldRespectTableFilters()
        {
            var dbPath = TestSupport.TempFile("scanfilter", ".db");
            var connectionString = CreateDatabase(dbPath);

            using var executor = new SqliteExecutor(connectionString);
            var scanner = new DatabaseScanner(executor);

            var onlyUserOptions = new ScanDatabaseOptions();
            onlyUserOptions.Tables.Add("nor_user");
            var onlyUser = scanner.Scan(onlyUserOptions);
            Assert.Single(onlyUser.Tables);
            Assert.Equal("nor_user", onlyUser.Tables[0].Name);

            var excludeOptions = new ScanDatabaseOptions();
            excludeOptions.ExcludeTables.Add("nor_user");
            var excluded = scanner.Scan(excludeOptions);
            Assert.Single(excluded.Tables);
            Assert.Equal("nor_order", excluded.Tables[0].Name);
        }

        [Fact]
        public void GenerateXml_ShouldEmitReusableBaseline()
        {
            var dbPath = TestSupport.TempFile("scanxml", ".db");
            var connectionString = CreateDatabase(dbPath);

            string xml;
            using (var executor = new SqliteExecutor(connectionString))
            {
                var scanner = new DatabaseScanner(executor);
                var result = scanner.Scan();
                xml = scanner.GenerateXml(result, "scanner", "2026-10-26");
            }

            Assert.Contains("<changeUnit id=\"00000000_000000_baseline\"", xml);
            Assert.Contains("<table name=\"nor_user\" action=\"CREATE\">", xml);
            Assert.Contains("<column name=\"id\" action=\"ADD\" type=\"Int64\"", xml);
            Assert.Contains("<index name=\"uk_user_name\" action=\"ADD\" unique=\"true\">", xml);
            Assert.Contains("<checkConstraint name=\"ck_balance\" action=\"ADD\" expression=\"&quot;balance&quot; &gt;= 0\"/>", xml);
            // SQLite 的 PRAGMA foreign_key_list 不返回约束名，扫描时按 表_列 生成
            Assert.Contains("<foreignKey name=\"fk_nor_order_user_id\" action=\"ADD\" columns=\"user_id\" refTable=\"nor_user\" refColumns=\"id\" onDelete=\"CASCADE\"/>", xml);

            // 生成的 XML 能被解析器接受，且列属性保真
            var unit = TestSupport.ParseXml(xml);
            Assert.Empty(unit.Diagnostics.Where(x => x.Code.StartsWith("E0")));

            var creates = unit.Operations.OfType<DbChronicle.Models.Operations.CreateTableOperation>().ToList();
            Assert.Equal(2, creates.Count);

            var create = creates.Single(x => x.Table == "nor_user");
            Assert.True(create.Columns.Single(x => x.Name == "id").AutoIncrement);
            Assert.Equal(DbChronicle.Common.Enums.LogicalType.Int64, create.Columns.Single(x => x.Name == "id").Type);
            Assert.False(create.Columns.Single(x => x.Name == "name").Nullable);

            var foreignKey = unit.Operations.OfType<DbChronicle.Models.Operations.ForeignKeyOperation>()
                .Single(x => x.Table == "nor_order");
            Assert.Equal("nor_user", foreignKey.RefTable);
            Assert.Equal("id", foreignKey.RefColumns[0]);
        }
    }
}
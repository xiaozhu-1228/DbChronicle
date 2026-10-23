using System.Linq;
using DbChronicle.Domain.Operations;
using Xunit;

namespace DbChronicle.Scanning.Tests
{
    public class ScannerTests
    {
        [Fact]
        public void Scan_ShouldFindOnlyEntitiesWithTableAttribute()
        {
            var result = Scan();

            Assert.Contains(result.Tables, x => x.Name == "nor_user");
            Assert.Contains(result.Tables, x => x.Name == "nor_order");
            Assert.DoesNotContain(result.Tables, x => x.Name == "NotAnEntity");
        }

        [Fact]
        public void Scan_ShouldMapClrTypes()
        {
            var table = Scan().Tables.First(x => x.Name == "nor_user");

            Assert.Equal(LogicalType.Int64, Column(table, "Id").Type);
            Assert.True(Column(table, "Id").PrimaryKey);

            Assert.Equal(LogicalType.Boolean, Column(table, "Active").Type);
            Assert.Equal(LogicalType.Decimal, Column(table, "Balance").Type);
            Assert.Equal(LogicalType.DateTime, Column(table, "CreatedAt").Type);
            Assert.Equal(LogicalType.DateTimeOffset, Column(table, "UpdatedAt").Type);
            Assert.Equal(LogicalType.Uuid, Column(table, "Uid").Type);
            Assert.Equal(LogicalType.Blob, Column(table, "Avatar").Type);
            Assert.Equal(LogicalType.Int32, Column(table, "Kind").Type);
            Assert.True(Column(table, "LoginCount").Nullable);
        }

        [Fact]
        public void Scan_ShouldReadColumnNameLengthAndComment()
        {
            var table = Scan().Tables.First(x => x.Name == "nor_user");
            var column = Column(table, "user_name");

            Assert.Equal(LogicalType.String, column.Type);
            Assert.Equal(64, column.Length);
            Assert.Equal("用户名", column.Comment);
        }

        [Fact]
        public void Scan_ShouldSkipNotMappedAndNavigationProperties()
        {
            var table = Scan().Tables.First(x => x.Name == "nor_user");

            Assert.DoesNotContain(table.Columns, x => x.Name == "DisplayName");
            Assert.DoesNotContain(table.Columns, x => x.Name == "Orders");
        }

        [Fact]
        public void Scan_ShouldHonorTypeName()
        {
            var table = Scan().Tables.First(x => x.Name == "nor_order");

            Assert.Equal(LogicalType.String, Column(table, "OrderNo").Type);
            Assert.Equal(32, Column(table, "OrderNo").Length);
            Assert.Equal(LogicalType.Json, Column(table, "Payload").Type);
        }

        [Fact]
        public void Scan_ShouldFallbackToTextForUnboundedString()
        {
            var table = Scan().Tables.First(x => x.Name == "nor_user");
            Assert.Equal(LogicalType.Text, Column(table, "Bio").Type);
        }

        [Fact]
        public void GenerateXml_ShouldEmitBaseline()
        {
            var scanner = new CSharpScanner();
            var result = scanner.ScanAssembly(typeof(ScannerTests).Assembly.Location);
            var xml = scanner.GenerateXml(result, "tester", "2026-10-22");

            Assert.Contains("id=\"00000000_000000_baseline\"", xml);
            Assert.Contains("release=\"00000000\"", xml);
            Assert.Contains("<table name=\"nor_user\" action=\"CREATE\"", xml);
            Assert.Contains("name=\"Id\" action=\"ADD\" type=\"Int64\"", xml);
            Assert.Contains("primaryKey=\"true\"", xml);
            Assert.Contains("</changeUnit>", xml);
        }

        private static ScanResult Scan()
        {
            var scanner = new CSharpScanner();
            return scanner.ScanAssembly(typeof(ScannerTests).Assembly.Location);
        }

        private static ScannedColumn Column(ScannedTable table, string name)
        {
            return table.Columns.First(x => x.Name == name);
        }
    }
}

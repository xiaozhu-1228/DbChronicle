using System.IO;
using System.Linq;

using DbChronicle.Scanner.Source;
using Xunit;

namespace DbChronicle.IntegrationTests
{
    /// <summary>源码扫描（Roslyn）：从 .cs 直接生成基线草案，无需先编译。</summary>
    public class SourceScannerTests
    {
        private const string EntitySource = @"
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Demo.Entities
{
    /// <summary>用户表</summary>
    [Table(""nor_user"")]
    public class UserEntity
    {
        [Key]
        public long Id { get; set; }

        [Column(""user_name"")]
        [MaxLength(64)]
        [Required]
        public string UserName { get; set; }

        public decimal? Amount { get; set; }

        public DateTime CreatedAt { get; set; }

        [NotMapped]
        public string Display { get; set; }

        public byte[] Avatar { get; set; }
    }

    [Table(""nor_order"")]
    public class OrderEntity
    {
        public long Id { get; set; }
        public string OrderNo { get; set; }
    }

    public class NotAnEntityClass
    {
        public int Value { get; set; }
    }
}
";

        [Fact]
        public void ScanDirectory_ShouldMapEntitiesFromSource()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            File.WriteAllText(Path.Combine(directory, "Entities.cs"), EntitySource);

            var result = new SourceScanner().ScanDirectory(directory);

            Assert.Equal(2, result.Tables.Count);

            var user = result.Tables.Single(x => x.Name == "nor_user");
            Assert.Equal(5, user.Columns.Count);
            Assert.DoesNotContain(user.Columns, x => x.Name == "Display");
            Assert.Equal(64, user.Columns.Single(x => x.Name == "user_name").Length);
            Assert.False(user.Columns.Single(x => x.Name == "user_name").Nullable);
            Assert.True(user.Columns.Single(x => x.Name == "Amount").Nullable);
            Assert.Contains("Id", user.PrimaryKeyColumns);

            var order = result.Tables.Single(x => x.Name == "nor_order");
            Assert.Equal(2, order.Columns.Count);
        }

        [Fact]
        public void ScanDirectory_ShouldSkipBuildOutput()
        {
            var directory = TestSupport.CreateMigrationsDirectory();
            var objDirectory = Path.Combine(directory, "obj");
            Directory.CreateDirectory(objDirectory);
            File.WriteAllText(Path.Combine(objDirectory, "Ignored.cs"), EntitySource);

            var result = new SourceScanner().ScanDirectory(directory);

            Assert.Empty(result.Tables);
        }

        [Fact]
        public void ScanDirectory_ShouldFail_WhenDirectoryMissing()
        {
            Assert.Throws<DirectoryNotFoundException>(() =>
                new SourceScanner().ScanDirectory(Path.Combine(Path.GetTempPath(), "not-exists-" + System.Guid.NewGuid().ToString("N"))));
        }
    }
}

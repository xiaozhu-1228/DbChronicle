using System.IO;
using System;

using DbChronicle.Configuration;
using DbChronicle.Migrations.State;
using Xunit;

namespace DbChronicle.Tests
{
    public class VersionStoreTests
    {
        [Fact]
        public void EnsureTable_ShouldCreateStateDirectory()
        {
            var config = CreateConfig(out var directory);

            new VersionStore(config).EnsureTable();

            Assert.True(Directory.Exists(Path.Combine(directory, "version_log", "test")));
        }

        [Fact]
        public void BeginAndMarkApplied_ShouldRoundTrip()
        {
            var config = CreateConfig(out var directory);
            var store = new VersionStore(config);

            store.Begin("20261022_100000_a1b2c3", "20261022", "checksum-1", "mysql", "tester", "desc");
            store.MarkApplied("20261022_100000_a1b2c3");

            var all = store.LoadAll();
            Assert.Single(all);
            Assert.Equal("20261022_100000_a1b2c3", all[0].ChangeId);
            Assert.Equal("checksum-1", all[0].Checksum);
            Assert.False(all[0].HasFailure);
            Assert.True(File.Exists(Path.Combine(directory, "version_log", "test", VersionStore.StateFileName)));
        }

        [Fact]
        public void MarkFailure_ShouldRecordStatementIndex()
        {
            var config = CreateConfig(out _);
            var store = new VersionStore(config);

            store.Begin("20261022_110000_d4e5f6", "20261022", "checksum-2", "mysql", "tester", "desc");
            store.MarkFailure("20261022_110000_d4e5f6", 3);

            var record = Assert.Single(store.LoadAll());
            Assert.True(record.HasFailure);
            Assert.Equal(3, record.FailureStatement);
        }

        [Fact]
        public void RecordStatement_ShouldTrackProgress()
        {
            var config = CreateConfig(out _);
            var store = new VersionStore(config);

            store.Begin("20261022_120000_a7b8c9", "20261022", "checksum-3", "mysql", "tester", "desc");
            store.RecordStatement("20261022_120000_a7b8c9", 0, "hash-0");
            store.RecordStatement("20261022_120000_a7b8c9", 1, "hash-1");

            var statements = store.LoadStatements("20261022_120000_a7b8c9");
            Assert.Equal(2, statements.Count);
            Assert.Equal("hash-1", statements[1]);

            store.ClearStatements("20261022_120000_a7b8c9");
            Assert.Empty(store.LoadStatements("20261022_120000_a7b8c9"));
        }

        [Fact]
        public void Delete_ShouldRemoveRecord()
        {
            var config = CreateConfig(out _);
            var store = new VersionStore(config);

            store.Begin("20261022_130000_112233", "20261022", "checksum-4", "mysql", "tester", "desc");
            store.Delete("20261022_130000_112233");

            Assert.Empty(store.LoadAll());
        }

        private static DbChronicleConfig CreateConfig(out string directory)
        {
            directory = Path.Combine(Path.GetTempPath(), "dbchronicle-state-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);

            var config = new DbChronicleConfig { BaseDirectory = directory };
            config.Database.Dialect = "mysql";
            config.Environment = "test";
            config.Paths.VersionLog = "./version_log/${environment}";
            return config;
        }
    }
}
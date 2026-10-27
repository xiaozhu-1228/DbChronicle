using System;

using DbChronicle.Executor.EfCore;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DbChronicle.IntegrationTests
{
    /// <summary>
    /// EF Core 执行器的构造层验证（不连库）：
    /// 真库执行由宿主提供的 DbContext + provider 决定，这里只钉住能力标志与工厂契约。
    /// </summary>
    public class EfCoreExecutorTests
    {
        private sealed class SampleContext : DbContext
        {
            public SampleContext(DbContextOptions options) : base(options)
            {
            }
        }

        private static SampleContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<SampleContext>()
                .UseSqlServer("Server=localhost;Database=sample;Trusted_Connection=True;")
                .Options;
            return new SampleContext(options);
        }

        [Fact]
        public void Executor_ShouldExposeDialectAndTransactionalDdl()
        {
            using var context = CreateContext();

            using var sqlServer = new EfCoreExecutor(context, "sqlserver");
            Assert.Equal("sqlserver", sqlServer.DialectName);
            Assert.True(sqlServer.SupportsTransactionalDdl);

            using var mySql = new EfCoreExecutor(context, "mysql");
            Assert.False(mySql.SupportsTransactionalDdl);
        }

        [Fact]
        public void Factory_ShouldCreateExecutor()
        {
            using var context = CreateContext();
            var factory = new EfCoreExecutorFactory(context, "postgresql");

            Assert.Equal("postgresql", factory.DialectName);
            using var executor = factory.Create("ignored");
            Assert.IsType<EfCoreExecutor>(executor);
            Assert.Equal("postgresql", executor.DialectName);
        }

        [Fact]
        public void Executor_ShouldRejectNullContext()
        {
            Assert.Throws<ArgumentNullException>(() => new EfCoreExecutor(null, "sqlite"));
        }

        [Fact]
        public void Commit_WithoutBegin_ShouldBeNoOp()
        {
            using var context = CreateContext();
            using var executor = new EfCoreExecutor(context, "sqlite");

            executor.Commit();
            executor.Rollback();
            executor.Dispose();
        }
    }
}
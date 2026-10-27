using DbChronicle.Configuration;
using DbChronicle.Demo.Infrastructure;
using DbChronicle.Embedded;
using DbChronicle.Executor.AdoNet;

namespace DbChronicle.Demo.Scenarios
{
    /// <summary>
    /// MySQL 演示：连接（连接串来自环境变量）→ 创建 IDbChronicle → DbUpgrade。
    /// </summary>
    internal sealed class MySqlMigrationScenario
    {
        private readonly ConsoleReporter _console;
        private readonly DbChronicleConfig _config;

        public MySqlMigrationScenario(ConsoleReporter console, DbChronicleConfig config)
        {
            _console = console;
            _config = config;
        }

        public void Run()
        {
            var factory = AdoNetExecutorFactory.Resolve(_config.Database.Dialect);
            var connectionString = DemoConnectionStrings.ResolveMySql();

            // 组件用法就这三步：连接 → 创建 IDbChronicle → DbUpgrade
            using (var executor = factory.Create(connectionString))
            using (var chronicle = DbChronicleFactory.Create(executor, new DbChronicleOptions
            {
                Config = _config,
                Log = _console.Line
            }))
            {
                var result = chronicle.DbUpgrade();
                _console.Line("DbUpgrade：success=" + result.Success
                    + "，应用 " + result.AppliedCount
                    + "，跳过 " + result.SkippedCount
                    + "，失败 " + result.FailedCount
                    + (string.IsNullOrEmpty(result.Message) ? "" : "，" + result.Message));
            }
        }
    }
}
using DbChronicle.Configuration;
using DbChronicle.Demo.Infrastructure;
using DbChronicle.Embedded;
using DbChronicle.Executor.AdoNet;

namespace DbChronicle.Demo.Scenarios
{
    /// <summary>
    /// PostgreSQL 演示：连接（连接串来自环境变量）→ 创建 IDbChronicle → DbUpgrade。
    /// </summary>
    internal sealed class PostgreSqlScenario
    {
        private readonly ConsoleReporter _console;
        private readonly DbChronicleConfig _config;

        public PostgreSqlScenario(ConsoleReporter console, DbChronicleConfig config)
        {
            _console = console;
            _config = config;
        }

        public bool Run()
        {
            var factory = AdoNetExecutorFactory.Resolve(_config.Database.Dialect);
            var connectionString = DemoConnectionStrings.ResolvePostgreSql();

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
                return result.Success;
            }
        }
    }
}
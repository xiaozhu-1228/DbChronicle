using System;
using System.IO;
using DbChronicle.Configuration;
using DbChronicle.Abstractions.Execution;
using DbChronicle.Runtime.Execution;
using DbChronicle.Abstractions.Versioning;
using DbChronicle.Runtime.Versioning;
using DbChronicle.Demo.Cli;
using DbChronicle.Demo.Infrastructure;
using DbChronicle.Demo.MySql;
using DbChronicle.Demo.Xml;
using DbChronicle.Executor.AdoNet;

namespace DbChronicle.Demo.Scenarios
{
    /// <summary>
    /// 嵌入式集成演示：宿主代码内构造配置并调用 DbChronicleMigrator，不经过 CLI。
    /// 使用独立演示库，避免与业务库的变更链冲突。
    /// </summary>
    internal sealed class EmbeddedScenario
    {
        private const string DemoDatabase = "dbchronicle_embedded_demo";
        private const string TargetTable = "dbchronicle_embedded_demo";

        private readonly DemoOptions _options;
        private readonly ConsoleReporter _console;
        private readonly ChangeUnitXmlWriter _xmlWriter = new ChangeUnitXmlWriter();

        public EmbeddedScenario(DemoOptions options, ConsoleReporter console)
        {
            _options = options;
            _console = console;
        }

        public void Run()
        {
            var connectionString = MySqlConnectionStrings.Resolve();
            var workspace = DemoWorkspace.Create("embedded", false);

            _console.Section("1/5 加载配置（--config 或代码内构造）");
            var config = ConfigSource.Resolve(_options, workspace, _console, "mysql", "8.0", "embedded", "embedded");
            config.Options.IncrementalModify = true;

            var factory = new AdoNetExecutorFactory(config.Database.Dialect);

            using (var admin = factory.Create(MySqlConnectionStrings.WithoutDatabase(connectionString)))
            {
                if (_options.Reset)
                {
                    admin.Execute("DROP DATABASE IF EXISTS `" + DemoDatabase + "`");
                    _console.Line("已重置演示库（--reset）：" + DemoDatabase);
                }

                admin.Execute("CREATE DATABASE IF NOT EXISTS `" + DemoDatabase + "` DEFAULT CHARACTER SET utf8mb4");
            }

            connectionString = MySqlConnectionStrings.WithDatabase(connectionString, DemoDatabase);
            _console.Line("演示库=" + DemoDatabase + "（与业务库隔离，可反复运行）");

            _console.Section("2/5 逻辑模型");
            if (_options.HasExternalConfig)
            {
                _console.Line("  使用 --config 指定目录中的变更单元（不自动生成）");
            }
            else
            {
                foreach (var path in _xmlWriter.WriteEmbeddedScenarioUnits(config))
                {
                    _console.Line("  " + Path.GetFileName(path));
                }
            }

            _console.Section("3/5 集成 Core：DbChronicleMigrator.Run（等价 apply，无 CLI）");
            var result = DbChronicleMigrator.Run(new MigratorOptions
            {
                Config = config,
                ConnectionString = connectionString,
                AppliedBy = "embedded-host",
                ExecutorFactory = factory,
                LockFactory = factory,
                DryRun = !_options.Apply,
                Log = _console.Log
            });

            _console.Line("结果：success=" + result.Success
                + "，应用 " + result.AppliedCount + "，跳过 " + result.SkippedCount
                + (string.IsNullOrEmpty(result.Message) ? "" : "，message=" + result.Message));

            if (!result.Success)
            {
                throw new InvalidOperationException(result.Message);
            }

            if (!_options.Apply)
            {
                _console.Section("4/5 已跳过（dry-run）");
                _console.Line("确认 SQL 无误后加 --apply 执行。");
                return;
            }

            _console.Section("4/5 版本状态（Core API + VersionStore）");
            using (var db = factory.Create(connectionString))
            {
                foreach (var record in new VersionStore(config).LoadAll())
                {
                    _console.Line(record.ChangeId + "  release=" + record.Release
                        + "  appliedAt=" + record.AppliedAt + "  checksum=" + ConsoleReporter.Short(record.Checksum));
                }
            }

            _console.Section("5/5 结构复核（Core 查询 information_schema）");
            using (var db = factory.Create(connectionString))
            {
                foreach (var column in new MySqlTableIntrospector(db).Inspect(TargetTable))
                {
                    _console.Line("  " + column);
                }
            }
        }
    }
}

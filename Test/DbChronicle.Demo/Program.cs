using System.Text;
using System.Linq;
using System;

using DbChronicle.Demo.Infrastructure;
using DbChronicle.Demo.OperationTests;
using DbChronicle.Demo.Scenarios;

namespace DbChronicle.Demo
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
                var console = ConsoleReporter.Console;
                var config = DemoConfig.Load(console);

                if (args.Length == 0)
                {
                    return new OperationTestRunner(console, config).Run(Array.Empty<string>());
                }

                if (string.Equals(args[0], "ops", StringComparison.OrdinalIgnoreCase))
                {
                    return new OperationTestRunner(console, config).Run(args.Skip(1).ToArray());
                }

                if (string.Equals(args[0], "demo", StringComparison.OrdinalIgnoreCase))
                {
                    switch ((config.Database.Dialect ?? "").Trim().ToLowerInvariant())
                    {
                        case "mysql":
                        case "mariadb":
                            return new MySqlMigrationScenario(console, config).Run() ? 0 : 1;

                        case "postgresql":
                            return new PostgreSqlScenario(console, config).Run() ? 0 : 1;

                        default:
                            console.Line("配置文件里的 dialect 不是 Demo 支持的方言：" + config.Database.Dialect
                                + "（当前支持 mysql / postgresql，请在 Config/DbchronicleConfig.xml 中修改）");
                            return 1;
                    }
                }

                console.Line("未知参数：" + args[0]);
                console.Line("  （无参数）= 13 类操作测试；demo = DbUpgrade 演示；ops [--dialect=...] = 指定方言跑操作测试");
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("demo 失败：" + ex.Message);
                return 1;
            }
        }
    }
}

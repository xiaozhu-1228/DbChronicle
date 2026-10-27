using System.Text;
using System;

using DbChronicle.Demo.Infrastructure;
using DbChronicle.Demo.Scenarios;

namespace DbChronicle.Demo
{
    internal static class Program
    {
        /// <summary>
        /// 方言完全取自 DbchronicleConfig.xml 的 &lt;database dialect="..."&gt;，
        /// 连接串只决定连哪个库（对应方言的环境变量）。无需任何命令行参数。
        /// </summary>
        private static int Main()
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
                var console = ConsoleReporter.Console;
                var config = DemoConfig.Load(console);

                switch ((config.Database.Dialect ?? "").Trim().ToLowerInvariant())
                {
                    case "mysql":
                    case "mariadb":
                        new MySqlMigrationScenario(console, config).Run();
                        return 0;

                    case "postgresql":
                        new PostgreSqlScenario(console, config).Run();
                        return 0;

                    default:
                        console.Line("配置文件里的 dialect 不是 Demo 支持的方言：" + config.Database.Dialect
                            + "（当前支持 mysql / postgresql，请在 Config/DbchronicleConfig.xml 中修改）");
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("demo 失败：" + ex.Message);
                return 1;
            }
        }
    }
}
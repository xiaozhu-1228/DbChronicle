using DbChronicle.Demo.Infrastructure;
using DbChronicle.Demo.Scenarios;

namespace DbChronicle.Demo.Cli
{
    internal static class DemoRunner
    {
        public static void Run(DemoOptions options)
        {
            var console = ConsoleReporter.Console;

            switch (options.Scenario)
            {
                case DemoScenario.Embedded:
                    new EmbeddedScenario(options, console).Run();
                    break;

                case DemoScenario.MySql:
                    new MySqlMigrationScenario(options, console).Run();
                    break;

                default:
                    new SqliteScenario(options, console).Run();
                    break;
            }
        }
    }
}

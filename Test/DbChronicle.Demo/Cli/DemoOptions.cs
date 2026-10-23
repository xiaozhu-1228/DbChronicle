using System;
using System.Linq;

namespace DbChronicle.Demo.Cli
{
    internal enum DemoScenario
    {
        Sqlite,
        MySql,
        Embedded
    }

    internal sealed class DemoOptions
    {
        public DemoScenario Scenario { get; private set; } = DemoScenario.MySql;

        public bool Apply { get; private set; }

        public bool Reset { get; private set; }

        /// <summary>--config 指定的配置文件路径；为空时按场景在临时工作区生成默认配置。</summary>
        public string ConfigPath { get; private set; }

        /// <summary>是否使用外部配置（此时不生成变更单元，完全复用配置指定的 migrations）。</summary>
        public bool HasExternalConfig
        {
            get { return !string.IsNullOrEmpty(ConfigPath); }
        }

        public static DemoOptions Parse(string[] args)
        {
            var options = new DemoOptions();
            if (args == null || args.Length == 0)
            {
                return options;
            }

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (Is(arg, "--embedded"))
                {
                    options.Scenario = DemoScenario.Embedded;
                }
                else if (Is(arg, "--mysql"))
                {
                    options.Scenario = DemoScenario.MySql;
                }
                else if (Is(arg, "--sqlite"))
                {
                    options.Scenario = DemoScenario.Sqlite;
                }
                else if (Is(arg, "--apply"))
                {
                    options.Apply = true;
                }
                else if (Is(arg, "--reset"))
                {
                    options.Reset = true;
                }
                else if (Is(arg, "--config"))
                {
                    if (i + 1 < args.Length)
                    {
                        options.ConfigPath = args[++i];
                    }
                }
                else if (arg.StartsWith("--config=", StringComparison.OrdinalIgnoreCase))
                {
                    options.ConfigPath = arg.Substring("--config=".Length);
                }
            }

            return options;
        }

        private static bool Is(string arg, string name)
        {
            return string.Equals(arg, name, StringComparison.OrdinalIgnoreCase);
        }
    }
}

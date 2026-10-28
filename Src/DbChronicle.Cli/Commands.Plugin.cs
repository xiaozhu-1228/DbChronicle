using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System;

using DbChronicle.Cli.Plugins;
using DbChronicle.Cli.Plugins.Loading;
using DbChronicle.Common.Enums;
using DbChronicle.Compilation.Checks;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Dialects;
using DbChronicle.Configuration;
using DbChronicle.Execution;
using DbChronicle.Migrations.Executor;
using DbChronicle.Migrations.Rollback;
using DbChronicle.Migrations.State;
using DbChronicle.Migrations.Templates;
using DbChronicle.Models;
using DbChronicle.Parsing;
using DbChronicle.Scanner;

namespace DbChronicle.Cli
{
    public static partial class Commands
    {
        public static int Plugin(CliArguments args)
        {
            var action = args.Positionals.Count > 0 ? args.Positionals[0].ToLowerInvariant() : "list";
            switch (action)
            {
                case "list":
                    return PluginList();
                case "disable":
                    return SetPluginState(args, true);
                case "enable":
                    return SetPluginState(args, false);
                default:
                    throw new CliFailureException(ExitCodes.Usage,
                        "未知 plugin 子命令：" + action + "（可用：list / disable / enable）");
            }
        }

        private static int PluginList()
        {
            var folders = Host.DiscoverPluginFolders();
            Console.WriteLine("插件目录：" + Host.PluginDirectory);
            Console.WriteLine("设置文件：" + PluginSettings.SettingsPath);
            Console.WriteLine();

            if (folders.Count == 0)
            {
                Console.WriteLine("（未部署任何插件；upgrade / scan-db / scan --source 会返回退出码 6）");
                return ExitCodes.Success;
            }

            foreach (var folder in folders)
            {
                Console.WriteLine((folder.Disabled ? "[已禁用] " : "[启用]   ") + folder.Name);
                foreach (var assembly in folder.Assemblies)
                {
                    Console.WriteLine("             " + assembly);
                }
            }

            Console.WriteLine();
            Console.WriteLine("启停：db-chronicle plugin disable|enable <name>");

            // 插件注册的命令（会触发一次加载；失败只提示，不影响 list 结果）
            try
            {
                var commands = Host.Commands;
                if (commands.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("插件命令：");
                    foreach (var item in commands.Values.OrderBy(x => x.Name))
                    {
                        Console.WriteLine("  " + item.Name + (string.IsNullOrEmpty(item.Help) ? "" : "  " + item.Help));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("（插件命令加载失败：" + ex.Message + "）");
            }

            return ExitCodes.Success;
        }

        private static int SetPluginState(CliArguments args, bool disable)
        {
            var verb = disable ? "disable" : "enable";
            if (args.Positionals.Count < 2)
            {
                throw new CliFailureException(ExitCodes.Usage, "用法：db-chronicle plugin " + verb + " <name>");
            }

            var name = args.Positionals[1];
            var settings = PluginSettings.Load();
            var changed = disable ? settings.Disable(name) : settings.Enable(name);
            if (changed)
            {
                settings.Save();
            }

            Console.WriteLine((disable ? "已禁用：" : "已启用：") + name + (changed ? "" : "（本来就是该状态）"));
            Console.WriteLine("设置文件：" + PluginSettings.SettingsPath);
            Host.Reload();
            return ExitCodes.Success;
        }

    }
}
using System;
using System.Collections.Generic;
using System.IO;

using DbChronicle.Cli.Plugins;
using DbChronicle.Configuration;

namespace DbChronicle.Cli
{
    /// <summary>
    /// 把 CLI 的参数 / 配置 / 输出流适配成 <see cref="ICommandContext"/>，
    /// 让插件命令不依赖 <see cref="CliArguments"/> 等内部类型。
    /// </summary>
    internal sealed class PluginCommandContext : ICommandContext
    {
        private readonly CliArguments _arguments;
        private DbChronicleConfig _config;

        public PluginCommandContext(CliArguments arguments)
        {
            _arguments = arguments;
        }

        public string Get(string name, string fallback = null)
        {
            return _arguments.Get(name, fallback);
        }

        public bool Has(string name)
        {
            return _arguments.Has(name);
        }

        public IReadOnlyList<string> Positional
        {
            get { return _arguments.Positionals; }
        }

        /// <summary>惰性加载：插件命令不碰配置时不会因为缺少 DbchronicleConfig.xml 而失败。</summary>
        public DbChronicleConfig Config
        {
            get { return _config ?? (_config = Commands.LoadConfigForPlugin(_arguments)); }
        }

        public TextWriter Out
        {
            get { return Console.Out; }
        }

        public TextWriter Error
        {
            get { return Console.Error; }
        }
    }
}

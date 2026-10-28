using System.Collections.Generic;
using System.IO;

using DbChronicle.Configuration;

namespace DbChronicle.Cli.Plugins
{
    /// <summary>
    /// 插件命令的执行上下文：只暴露"读参数 + 读写输出 + 配置"，不泄漏 CLI 内部类型，
    /// 保证插件不会依赖 CLI 的实现细节。
    /// </summary>
    public interface ICommandContext
    {
        /// <summary>取具名参数（--name value），缺省返回 fallback。</summary>
        string Get(string name, string fallback = null);

        /// <summary>是否出现该开关（--name）。</summary>
        bool Has(string name);

        /// <summary>位置参数（不含命令行第一个命令名）。</summary>
        IReadOnlyList<string> Positional { get; }

        /// <summary>已加载的配置。</summary>
        DbChronicleConfig Config { get; }

        /// <summary>标准输出。</summary>
        TextWriter Out { get; }

        /// <summary>标准错误。</summary>
        TextWriter Error { get; }
    }
}

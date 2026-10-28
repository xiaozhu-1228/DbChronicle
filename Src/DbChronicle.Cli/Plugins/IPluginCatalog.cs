using System;
using System.Collections.Generic;

using DbChronicle.CodeGen;
using DbChronicle.Execution;

namespace DbChronicle.Cli.Plugins
{
    /// <summary>插件命令的描述。</summary>
    public sealed class PluginCommand
    {
        public string Name { get; set; }

        public string Help { get; set; }

        public Func<ICommandContext, int> Handler { get; set; }
    }

    /// <summary>
    /// 能力目录（宿主侧）：按方言 / 语言取能力。缺失时抛
    /// <see cref="PluginNotInstalledException"/>（带"该装哪个包"的提示），
    /// 而不是把程序集加载异常抛给用户。
    /// </summary>
    public interface IPluginCatalog
    {
        /// <summary>取执行器工厂。</summary>
        IDbExecutorFactory ResolveExecutor(string dialect);

        /// <summary>取库级锁工厂（可能为 null，方言不支持时）。</summary>
        IDbLockFactory ResolveLocks(string dialect);

        /// <summary>取源码扫描器。</summary>
        ISourceScanner ResolveSourceScanner(string language);

        /// <summary>取代码生成器（内建优先，插件可覆盖）。</summary>
        IEntityCodeWriter ResolveCodeGenerator(string language);

        /// <summary>已注册的执行器方言。</summary>
        IReadOnlyCollection<string> ExecutorDialects { get; }

        /// <summary>已注册的扫描器语言。</summary>
        IReadOnlyCollection<string> ScannerLanguages { get; }

        /// <summary>插件注册的命令（名称 → 描述）。</summary>
        IReadOnlyDictionary<string, PluginCommand> Commands { get; }
    }
}

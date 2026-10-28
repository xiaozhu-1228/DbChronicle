using System;

using DbChronicle.CodeGen;
using DbChronicle.Execution;

namespace DbChronicle.Cli.Plugins
{
    /// <summary>
    /// 插件注册表：插件在 <c>Initialize</c> 中把自己的能力登记进来，宿主按需取用。
    /// </summary>
    public interface IPluginRegistry
    {
        /// <summary>注册某方言的执行器工厂（可选同时提供库级锁）。</summary>
        void AddExecutor(string dialect, Func<IDbExecutorFactory> executor, Func<IDbLockFactory> locks = null);

        /// <summary>注册某语言的源码扫描器。</summary>
        void AddSourceScanner(string language, Func<ISourceScanner> scanner);

        /// <summary>注册某语言的实体代码生成器（可覆盖内建实现）。</summary>
        void AddCodeGenerator(string language, Func<IEntityCodeWriter> writer);

        /// <summary>注册一个全新的子命令（如 docgen / lint）。</summary>
        void AddCommand(string name, Func<ICommandContext, int> handler, string help);
    }
}

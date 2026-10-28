using System;
using System.Collections.Generic;
using System.Reflection;

namespace DbChronicle.Cli.Plugins.Loading
{
    /// <summary>
    /// 一次"插件已加载"的作用域：持有可卸载的加载上下文与能力目录。
    /// <see cref="Dispose"/> 先清空注册表（断开对插件实例的引用）再 <c>Unload</c>。
    /// </summary>
    internal sealed class PluginScope : IDisposable
    {
        public PluginScope(IReadOnlyList<string> searchDirectories)
        {
            Context = new PluginLoadContext(searchDirectories);
            Catalog = new PluginCatalog();
        }

        public PluginLoadContext Context { get; }

        public PluginCatalog Catalog { get; }

        public Assembly LoadAssembly(string path)
        {
            return Context.LoadFromAssemblyPath(path);
        }

        public void Dispose()
        {
            Catalog.Clear();
            Context.Unload();
        }
    }
}

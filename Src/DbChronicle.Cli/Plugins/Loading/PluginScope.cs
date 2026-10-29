using System;
using System.Collections.Generic;
using System.Reflection;

#if NETFRAMEWORK
using System.IO;
#endif

namespace DbChronicle.Cli.Plugins.Loading
{
#if NETFRAMEWORK
    /// <summary>
    /// 一次"插件已加载"的作用域：.NET Framework 没有可卸载的 <c>AssemblyLoadContext</c>，
    /// 插件用 <c>Assembly.LoadFrom</c> 载入（不可卸载，<see cref="Dispose"/> 只断开注册表引用）。
    /// 依赖从搜索目录列表经 <c>AppDomain.AssemblyResolve</c> 解析。
    /// </summary>
    internal sealed class PluginScope : IDisposable
    {
        private readonly IReadOnlyList<string> _searchDirectories;

        public PluginScope(IReadOnlyList<string> searchDirectories)
        {
            _searchDirectories = searchDirectories ?? (IReadOnlyList<string>)Array.Empty<string>();
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
            Catalog = new PluginCatalog();
        }

        public PluginCatalog Catalog { get; }

        public Assembly LoadAssembly(string path)
        {
            var requested = AssemblyName.GetAssemblyName(path);
            foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(loaded.GetName().Name, requested.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return loaded;
                }
            }

            return Assembly.LoadFrom(path);
        }

        public void Dispose()
        {
            AppDomain.CurrentDomain.AssemblyResolve -= OnAssemblyResolve;
            Catalog.Clear();
        }

        private Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name).Name;
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            foreach (var directory in _searchDirectories)
            {
                var candidate = Path.Combine(directory, name + ".dll");
                if (File.Exists(candidate))
                {
                    return Assembly.LoadFrom(candidate);
                }
            }

            return null;
        }
    }
#else
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
#endif
}

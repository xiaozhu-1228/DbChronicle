#if !NETFRAMEWORK
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace DbChronicle.Cli.Plugins.Loading
{
    /// <summary>
    /// 插件专用加载上下文（可卸载）：插件及其依赖从**搜索目录列表**解析
    /// （每个插件可独占一个子目录，自带依赖与 runtimes），但**宿主与核心库一律交回默认上下文**，
    /// 避免加载出第二份同名程序集（否则会出现 <c>InvalidCastException: 无法将 A 转换为 A</c>）。
    /// </summary>
    internal sealed class PluginLoadContext : AssemblyLoadContext
    {
        private static readonly string[] NativeRids =
        {
            RuntimeInformation.RuntimeIdentifier, "win-x64", "win-x86", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"
        };

        private readonly IReadOnlyList<string> _searchDirectories;

        public PluginLoadContext(IReadOnlyList<string> searchDirectories)
            : base("DbChroniclePlugins", isCollectible: true)
        {
            _searchDirectories = searchDirectories ?? (IReadOnlyList<string>)Array.Empty<string>();
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            if (IsHostAssembly(assemblyName.Name))
            {
                return null;   // 返回 null 表示"由默认上下文处理"
            }

            foreach (var directory in _searchDirectories)
            {
                var candidate = Path.Combine(directory, assemblyName.Name + ".dll");
                if (File.Exists(candidate))
                {
                    return LoadFromAssemblyPath(candidate);
                }
            }

            return null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var fileName = unmanagedDllName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? unmanagedDllName
                : unmanagedDllName + ".dll";

            foreach (var directory in _searchDirectories)
            {
                foreach (var rid in NativeRids)
                {
                    if (string.IsNullOrEmpty(rid))
                    {
                        continue;
                    }

                    var candidate = Path.Combine(directory, "runtimes", rid, "native", fileName);
                    if (File.Exists(candidate))
                    {
                        return LoadUnmanagedDllFromPath(candidate);
                    }
                }

                var flat = Path.Combine(directory, fileName);
                if (File.Exists(flat))
                {
                    return LoadUnmanagedDllFromPath(flat);
                }
            }

            return IntPtr.Zero;
        }

        private static bool IsHostAssembly(string name)
        {
            return string.Equals(name, "db-chronicle", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "DbChronicle", StringComparison.OrdinalIgnoreCase);
        }
    }
}
#endif

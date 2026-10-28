using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using DbChronicle.Cli.Plugins;
using DbChronicle.CodeGen;
using DbChronicle.Execution;

namespace DbChronicle.Cli.Plugins.Loading
{
    /// <summary>
    /// 插件宿主：**按需加载**——启动时不碰任何插件 dll，只有命令真正需要某能力时才加载
    /// 提供该能力的那一个程序集。
    /// <para>
    /// 目录布局：<c>plugins/&lt;插件名&gt;/*.dll</c>（每插件独占子目录，自带依赖与 runtimes），
    /// 同时兼容 <c>plugins/*.dll</c> 的扁平布局。被 <c>plugin disable</c> 的目录会被跳过。
    /// </para>
    /// </summary>
    public sealed class PluginHost : IDisposable
    {
        /// <summary>插件目录名（与可执行文件同级）。</summary>
        public const string PluginDirectoryName = "plugins";

        private static readonly string[] InfrastructureFolders = { "runtimes", "ref", "resources" };

        private readonly string _pluginRoot;
        private readonly bool _unloadAfterUse;
        private readonly object _sync = new object();
        private readonly HashSet<string> _attempted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _warnings = new List<string>();
        private IReadOnlyList<string> _searchDirectories;
        private PluginScope _scope;

        /// <param name="pluginDirectory">插件根目录；null 表示 <c>&lt;exe&gt;/plugins</c>（不存在则回退 <c>&lt;exe&gt;</c>）。</param>
        /// <param name="unloadAfterUse">true 表示命令结束后卸载插件（默认 false）。</param>
        public PluginHost(string pluginDirectory = null, bool unloadAfterUse = false)
        {
            if (!string.IsNullOrEmpty(pluginDirectory))
            {
                _pluginRoot = Path.GetFullPath(pluginDirectory);
            }
            else
            {
                var baseDirectory = AppContext.BaseDirectory;
                var nested = Path.Combine(baseDirectory, PluginDirectoryName);
                _pluginRoot = Directory.Exists(nested) ? nested : baseDirectory;
            }

            _unloadAfterUse = unloadAfterUse;
        }

        /// <summary>插件根目录的绝对路径。</summary>
        public string PluginDirectory
        {
            get { return _pluginRoot; }
        }

        /// <summary>是否在命令结束后卸载插件。</summary>
        public bool UnloadAfterUse
        {
            get { return _unloadAfterUse; }
        }

        /// <summary>实际参与依赖解析的目录（根 + 各插件子目录，已排除禁用项）。</summary>
        public IReadOnlyList<string> SearchDirectories
        {
            get
            {
                lock (_sync)
                {
                    return _searchDirectories ?? (_searchDirectories = BuildSearchDirectories());
                }
            }
        }

        /// <summary>加载/初始化过程中的提示（版本不匹配、冲突等）。</summary>
        public IReadOnlyList<string> Warnings
        {
            get
            {
                lock (_sync)
                {
                    return new List<string>(_warnings);
                }
            }
        }

        /// <summary>当前能力目录（未加载任何插件时为空目录，不会抛异常）。</summary>
        public IPluginCatalog Catalog
        {
            get { return Scope.Catalog; }
        }

        public IDbExecutorFactory ResolveExecutor(string dialect)
        {
            EnsureLoaded(CapabilityKind.Executor, dialect);
            return Scope.Catalog.ResolveExecutor(dialect);
        }

        public IDbLockFactory ResolveLocks(string dialect)
        {
            EnsureLoaded(CapabilityKind.Executor, dialect);
            return Scope.Catalog.ResolveLocks(dialect);
        }

        public ISourceScanner ResolveSourceScanner(string language)
        {
            EnsureLoaded(CapabilityKind.Scanner, language);
            return Scope.Catalog.ResolveSourceScanner(language);
        }

        /// <summary>取代码生成器：返回 null 表示用核心库内建实现。</summary>
        public IEntityCodeWriter ResolveCodeGenerator(string language)
        {
            EnsureLoaded(CapabilityKind.CodeGenerator, language);
            return Scope.Catalog.ResolveCodeGenerator(language);
        }

        /// <summary>插件注册的命令（会触发一次全量加载探测）。</summary>
        public IReadOnlyDictionary<string, PluginCommand> Commands
        {
            get
            {
                EnsureLoaded(CapabilityKind.Command, null);
                return Scope.Catalog.Commands;
            }
        }

        /// <summary>
        /// 发现插件目录（供 <c>plugin list</c>）：扫描根目录下的子目录与根目录自身的插件程序集。
        /// </summary>
        public IReadOnlyList<PluginFolder> DiscoverPluginFolders()
        {
            var settings = PluginSettings.Load();
            var result = new List<PluginFolder>();
            if (!Directory.Exists(_pluginRoot))
            {
                return result;
            }

            var flatAssemblies = CollectPluginAssemblies(_pluginRoot);
            if (flatAssemblies.Count > 0)
            {
                result.Add(new PluginFolder
                {
                    Name = "(root)",
                    Path = _pluginRoot,
                    Disabled = false,
                    Assemblies = flatAssemblies
                });
            }

            foreach (var directory in Directory.GetDirectories(_pluginRoot))
            {
                var name = Path.GetFileName(directory);
                if (IsInfrastructureFolder(name))
                {
                    continue;
                }

                var assemblies = CollectPluginAssemblies(directory);
                if (assemblies.Count == 0)
                {
                    continue;
                }

                result.Add(new PluginFolder
                {
                    Name = name,
                    Path = directory,
                    Disabled = settings.IsDisabled(name),
                    Assemblies = assemblies
                });
            }

            return result;
        }

        /// <summary>清空已加载状态并重新发现目录（<c>plugin enable/disable</c> 之后调用）。</summary>
        public void Reload()
        {
            lock (_sync)
            {
                if (_scope != null)
                {
                    _scope.Dispose();
                    _scope = null;
                }

                _attempted.Clear();
                _warnings.Clear();
                _searchDirectories = null;
            }
        }

        /// <summary>卸载已加载插件（collectible ALC），下次需要时重新加载。</summary>
        public void UnloadAll()
        {
            Reload();
        }

        public void Dispose()
        {
            Reload();
        }

        private PluginScope Scope
        {
            get
            {
                lock (_sync)
                {
                    return _scope ?? (_scope = new PluginScope(SearchDirectories));
                }
            }
        }

        private enum CapabilityKind
        {
            Executor,
            Scanner,
            CodeGenerator,
            Command
        }

        private void EnsureLoaded(CapabilityKind kind, string key)
        {
            lock (_sync)
            {
                if (IsSatisfied(kind, key))
                {
                    return;
                }

                foreach (var assemblyName in Candidates(kind, key))
                {
                    if (!TryLoadPlugin(assemblyName))
                    {
                        continue;
                    }

                    if (IsSatisfied(kind, key))
                    {
                        return;
                    }
                }
            }
        }

        private bool IsSatisfied(CapabilityKind kind, string key)
        {
            var catalog = Scope.Catalog;
            switch (kind)
            {
                case CapabilityKind.Executor:
                    return catalog.HasExecutor(key);

                case CapabilityKind.Scanner:
                    return catalog.HasScanner(key);

                case CapabilityKind.CodeGenerator:
                    return catalog.ResolveCodeGenerator(key) != null;

                case CapabilityKind.Command:
                    return catalog.Commands.Count > 0;

                default:
                    return false;
            }
        }

        /// <summary>能力 → 候选插件程序集（按文件名约定，避免启动时加载全部分析）。</summary>
        private static IEnumerable<string> Candidates(CapabilityKind kind, string key)
        {
            if (kind == CapabilityKind.Executor)
            {
                // EfCore 执行器需要宿主的 DbContext，无法由 CLI 自动构造，只作应用内嵌使用
                yield return "DbChronicle.Executor.AdoNet";
                yield break;
            }

            if (kind == CapabilityKind.Command)
            {
                yield return "DbChronicle.Executor.AdoNet";
                yield return "DbChronicle.Scanner.Source";
                yield return "DbChronicle.Scanner.Languages";
                yield break;
            }

            if (string.Equals((key ?? "").Trim(), "csharp", StringComparison.OrdinalIgnoreCase))
            {
                yield return "DbChronicle.Scanner.Source";
            }

            yield return "DbChronicle.Scanner.Languages";
        }

        private bool TryLoadPlugin(string assemblyName)
        {
            if (!_attempted.Add(assemblyName))
            {
                return false;   // 已尝试过（成功或失败都不再重试）
            }

            var path = LocateAssembly(assemblyName);
            if (path == null)
            {
                return false;
            }

            var scope = Scope;
            Assembly assembly;
            try
            {
                assembly = scope.LoadAssembly(path);
            }
            catch (Exception ex)
            {
                _warnings.Add("加载插件失败 " + assemblyName + "：" + ex.Message);
                return false;
            }

            foreach (var type in SafeTypes(assembly))
            {
                var attribute = (DbChroniclePluginAttribute)Attribute.GetCustomAttribute(
                    type, typeof(DbChroniclePluginAttribute), false);
                if (attribute == null)
                {
                    continue;
                }

                if (!IsVersionCompatible(attribute))
                {
                    _warnings.Add("插件 " + (attribute.Name ?? type.Name) + " 需要宿主版本 "
                        + attribute.MinHostVersion + "，当前 " + HostVersion + "，已跳过");
                    continue;
                }

                var initialize = type.GetMethod("Initialize",
                    BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(IPluginRegistry) }, null);
                if (initialize == null)
                {
                    _warnings.Add("插件 " + type.FullName + " 缺少 public static void Initialize(IPluginRegistry)");
                    continue;
                }

                try
                {
                    scope.Catalog.CurrentPlugin = attribute.Name ?? type.Name;
                    scope.Catalog.SetCurrent(attribute.Order);
                    initialize.Invoke(null, new object[] { scope.Catalog });
                }
                catch (TargetInvocationException ex)
                {
                    var inner = ex.InnerException ?? ex;
                    _warnings.Add("插件 " + (attribute.Name ?? type.Name) + " 初始化失败：" + inner.Message);
                }
            }

            foreach (var conflict in scope.Catalog.Conflicts)
            {
                if (!_warnings.Contains(conflict))
                {
                    _warnings.Add(conflict);
                }
            }

            return true;
        }

        private string LocateAssembly(string assemblyName)
        {
            var fileName = assemblyName + ".dll";
            foreach (var directory in SearchDirectories)
            {
                var candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            return null;
        }

        private IReadOnlyList<string> BuildSearchDirectories()
        {
            var result = new List<string>();
            if (!Directory.Exists(_pluginRoot))
            {
                return result;
            }

            // 根目录：兼容扁平布局（plugins/*.dll 或 <exe>/*.dll）
            result.Add(_pluginRoot);

            var settings = PluginSettings.Load();
            foreach (var directory in Directory.GetDirectories(_pluginRoot).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(directory);
                if (IsInfrastructureFolder(name))
                {
                    continue;
                }

                if (settings.IsDisabled(name))
                {
                    continue;
                }

                result.Add(directory);
            }

            return result;
        }

        private static bool IsInfrastructureFolder(string name)
        {
            foreach (var item in InfrastructureFolders)
            {
                if (string.Equals(name, item, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static List<string> CollectPluginAssemblies(string directory)
        {
            var result = new List<string>();
            if (!Directory.Exists(directory))
            {
                return result;
            }

            foreach (var file in Directory.GetFiles(directory, "DbChronicle.*.dll"))
            {
                var name = Path.GetFileName(file);
                if (string.Equals(name, "DbChronicle.dll", StringComparison.OrdinalIgnoreCase))
                {
                    continue;   // 核心库由宿主提供，不算插件
                }
                result.Add(name);
            }
            return result;
        }

        private static IEnumerable<Type> SafeTypes(Assembly assembly)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types ?? Type.EmptyTypes;
            }
            catch (Exception)
            {
                yield break;
            }

            foreach (var type in types)
            {
                if (type != null && type.IsClass && type.IsAbstract && type.IsSealed)
                {
                    yield return type;
                }
            }
        }

        private static bool IsVersionCompatible(DbChroniclePluginAttribute attribute)
        {
            if (string.IsNullOrEmpty(attribute.MinHostVersion))
            {
                return true;
            }

            Version required;
            if (!Version.TryParse(attribute.MinHostVersion, out required))
            {
                return true;
            }
            return HostVersion >= required;
        }

        private static Version HostVersion
        {
            get
            {
                var version = typeof(PluginHost).Assembly.GetName().Version;
                return version ?? new Version(1, 0, 0, 0);
            }
        }
    }
}

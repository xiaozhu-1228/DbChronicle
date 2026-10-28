using System;
using System.Collections.Generic;

using DbChronicle.Cli.Plugins;
using DbChronicle.CodeGen;
using DbChronicle.Execution;

namespace DbChronicle.Cli.Plugins.Loading
{
    /// <summary>
    /// 能力目录实现（同时是插件注册表）：同一能力被多个插件注册时，按
    /// <see cref="DbChroniclePluginAttribute.Order"/> 大者胜；同 Order 时后者覆盖并记录冲突。
    /// </summary>
    internal sealed class PluginCatalog : IPluginCatalog, IPluginRegistry
    {
        private sealed class Entry<T>
        {
            public Func<T> Factory;
            public int Order;
            public string Plugin;
        }

        private readonly Dictionary<string, Entry<IDbExecutorFactory>> _executors =
            new Dictionary<string, Entry<IDbExecutorFactory>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, Entry<IDbLockFactory>> _locks =
            new Dictionary<string, Entry<IDbLockFactory>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, Entry<ISourceScanner>> _scanners =
            new Dictionary<string, Entry<ISourceScanner>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, Entry<IEntityCodeWriter>> _codegens =
            new Dictionary<string, Entry<IEntityCodeWriter>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, PluginCommand> _commands =
            new Dictionary<string, PluginCommand>(StringComparer.OrdinalIgnoreCase);

        private readonly List<string> _conflicts = new List<string>();
        private readonly object _sync = new object();

        /// <summary>当前正在初始化的插件名（由 PluginHost 在调用 Initialize 前设置）。</summary>
        internal string CurrentPlugin { get; set; }

        /// <summary>注册过程中的冲突提示（供 --strict-plugins 判定）。</summary>
        internal IReadOnlyList<string> Conflicts
        {
            get
            {
                lock (_sync)
                {
                    return new List<string>(_conflicts);
                }
            }
        }

        public void AddExecutor(string dialect, Func<IDbExecutorFactory> executor, Func<IDbLockFactory> locks = null)
        {
            if (string.IsNullOrEmpty(dialect) || executor == null)
            {
                return;
            }

            lock (_sync)
            {
                Put(_executors, dialect, executor, "执行器 " + dialect);
                if (locks != null)
                {
                    Put(_locks, dialect, locks, "库级锁 " + dialect);
                }
            }
        }

        public void AddSourceScanner(string language, Func<ISourceScanner> scanner)
        {
            if (string.IsNullOrEmpty(language) || scanner == null)
            {
                return;
            }

            lock (_sync)
            {
                Put(_scanners, language, scanner, "扫描器 " + language);
            }
        }

        public void AddCodeGenerator(string language, Func<IEntityCodeWriter> writer)
        {
            if (string.IsNullOrEmpty(language) || writer == null)
            {
                return;
            }

            lock (_sync)
            {
                Put(_codegens, language, writer, "代码生成器 " + language);
            }
        }

        public void AddCommand(string name, Func<ICommandContext, int> handler, string help)
        {
            if (string.IsNullOrEmpty(name) || handler == null)
            {
                return;
            }

            lock (_sync)
            {
                _commands[name.Trim()] = new PluginCommand { Name = name.Trim(), Help = help, Handler = handler };
            }
        }

        public bool HasExecutor(string dialect)
        {
            lock (_sync)
            {
                return _executors.ContainsKey(Normalize(dialect));
            }
        }

        public bool HasScanner(string language)
        {
            lock (_sync)
            {
                return _scanners.ContainsKey(Normalize(language));
            }
        }

        public IDbExecutorFactory ResolveExecutor(string dialect)
        {
            Entry<IDbExecutorFactory> entry;
            lock (_sync)
            {
                _executors.TryGetValue(Normalize(dialect), out entry);
            }

            if (entry == null)
            {
                throw new PluginNotInstalledException(
                    "数据库执行器（" + dialect + "）",
                    "请把 DbChronicle.Executor.AdoNet.dll 部署到 " + PluginHost.PluginDirectoryName
                    + " 目录（或注入自有执行器）");
            }
            return entry.Factory();
        }

        public IDbLockFactory ResolveLocks(string dialect)
        {
            Entry<IDbLockFactory> entry;
            lock (_sync)
            {
                _locks.TryGetValue(Normalize(dialect), out entry);
            }
            return entry == null ? null : entry.Factory();
        }

        public ISourceScanner ResolveSourceScanner(string language)
        {
            Entry<ISourceScanner> entry;
            lock (_sync)
            {
                _scanners.TryGetValue(Normalize(language), out entry);
            }

            if (entry == null)
            {
                var hint = string.Equals(Normalize(language), "csharp", StringComparison.Ordinal)
                    ? "请把 DbChronicle.Scanner.Source.dll 部署到 " + PluginHost.PluginDirectoryName + " 目录（Roslyn 源码扫描）"
                    : "请把 DbChronicle.Scanner.Languages.dll 部署到 " + PluginHost.PluginDirectoryName + " 目录（Java / Go / TypeScript 扫描）";
                throw new PluginNotInstalledException("源码扫描器（" + language + "）", hint);
            }
            return entry.Factory();
        }

        /// <summary>返回 null 表示"用内建生成器"（核心库已支持 C# / Java / Go / TypeScript / C++）。</summary>
        public IEntityCodeWriter ResolveCodeGenerator(string language)
        {
            Entry<IEntityCodeWriter> entry;
            lock (_sync)
            {
                _codegens.TryGetValue(Normalize(language), out entry);
            }
            return entry == null ? null : entry.Factory();
        }

        public IReadOnlyCollection<string> ExecutorDialects
        {
            get
            {
                lock (_sync)
                {
                    return new List<string>(_executors.Keys);
                }
            }
        }

        public IReadOnlyCollection<string> ScannerLanguages
        {
            get
            {
                lock (_sync)
                {
                    return new List<string>(_scanners.Keys);
                }
            }
        }

        public IReadOnlyDictionary<string, PluginCommand> Commands
        {
            get
            {
                lock (_sync)
                {
                    return new Dictionary<string, PluginCommand>(_commands, StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        /// <summary>清空全部注册（Scope 释放前调用，断开对插件实例的引用以便卸载）。</summary>
        internal void Clear()
        {
            lock (_sync)
            {
                _executors.Clear();
                _locks.Clear();
                _scanners.Clear();
                _codegens.Clear();
                _commands.Clear();
                _conflicts.Clear();
            }
        }

        private void Put<T>(Dictionary<string, Entry<T>> target, string key, Func<T> factory, string label)
        {
            Entry<T> existing;
            var order = _currentOrder;
            if (target.TryGetValue(key, out existing))
            {
                if (existing.Order > order)
                {
                    _conflicts.Add(label + "：保留 " + existing.Plugin + "（Order=" + existing.Order
                        + "），忽略 " + (CurrentPlugin ?? "未知插件") + "（Order=" + order + "）");
                    return;
                }

                _conflicts.Add(label + "：" + (CurrentPlugin ?? "未知插件") + "（Order=" + order
                    + "）覆盖 " + existing.Plugin + "（Order=" + existing.Order + "）");
            }

            target[key] = new Entry<T> { Factory = factory, Order = order, Plugin = CurrentPlugin ?? "未知插件" };
        }

        private int _currentOrder;

        internal void SetCurrent(int order)
        {
            _currentOrder = order;
        }

        private static string Normalize(string value)
        {
            return (value ?? "").Trim().ToLowerInvariant();
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DbChronicle.Cli.Plugins
{
    /// <summary>
    /// 用户级插件设置（当前只有"禁用名单"）。
    /// <para>
    /// 存在用户目录而不是插件目录：<c>dotnet tool update</c> 会重写插件目录，
    /// 若把禁用状态写在插件目录里，升级后就被恢复了。
    /// </para>
    /// </summary>
    public sealed class PluginSettings
    {
        private const string DirectoryName = "DbChronicle";
        private const string FileName = "settings.json";
        private const string DisabledKey = "Disabled";

        /// <summary>被禁用的插件名（目录名，大小写无关）。</summary>
        public List<string> Disabled { get; set; } = new List<string>();

        /// <summary>设置目录：Windows 为 %LOCALAPPDATA%\DbChronicle，其余为 ~/.local/share/DbChronicle。</summary>
        public static string SettingsDirectory
        {
            get { return DirectoryOverride ?? ComputeSettingsDirectory(); }
        }

        /// <summary>测试用：临时覆盖设置目录（置 null 恢复默认）。</summary>
        internal static string DirectoryOverride { get; set; }

        private static string ComputeSettingsDirectory()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DirectoryName);
            }

            var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrEmpty(dataHome))
            {
                dataHome = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            }
            return Path.Combine(dataHome, DirectoryName);
        }

        public static string SettingsPath
        {
            get { return Path.Combine(SettingsDirectory, FileName); }
        }

        public static PluginSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var settings = new PluginSettings();
                    foreach (var item in ParseStringArray(json, DisabledKey))
                    {
                        if (!settings.IsDisabled(item))
                        {
                            settings.Disabled.Add(item);
                        }
                    }
                    return settings;
                }
            }
            catch (Exception)
            {
                // 配置损坏不应阻断命令，按"无禁用"处理
            }

            return new PluginSettings();
        }

        public void Save()
        {
            var directory = SettingsDirectory;
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var builder = new StringBuilder();
            builder.AppendLine("{");
            if (Disabled.Count == 0)
            {
                builder.AppendLine("  \"" + DisabledKey + "\": []");
            }
            else
            {
                builder.AppendLine("  \"" + DisabledKey + "\": [");
                for (var i = 0; i < Disabled.Count; i++)
                {
                    builder.Append("    \"").Append(Escape(Disabled[i])).Append('"');
                    builder.AppendLine(i == Disabled.Count - 1 ? "" : ",");
                }
                builder.AppendLine("  ]");
            }
            builder.AppendLine("}");

            File.WriteAllText(SettingsPath, builder.ToString(), new UTF8Encoding(false));
        }

        public bool IsDisabled(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            foreach (var item in Disabled)
            {
                if (string.Equals(item, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>禁用；返回 false 表示原本已在名单里。</summary>
        public bool Disable(string name)
        {
            if (IsDisabled(name))
            {
                return false;
            }
            Disabled.Add(name);
            return true;
        }

        /// <summary>启用；返回 false 表示原本不在名单里。</summary>
        public bool Enable(string name)
        {
            for (var i = Disabled.Count - 1; i >= 0; i--)
            {
                if (string.Equals(Disabled[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    Disabled.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        private static IEnumerable<string> ParseStringArray(string json, string key)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(json))
            {
                return result;
            }

            var keyIndex = json.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
            if (keyIndex < 0)
            {
                return result;
            }

            var open = json.IndexOf('[', keyIndex);
            if (open < 0)
            {
                return result;
            }

            var close = json.IndexOf(']', open);
            if (close < 0)
            {
                return result;
            }

            var body = json.Substring(open + 1, close - open - 1);
            var current = new StringBuilder();
            var inString = false;
            var escaped = false;

            foreach (var ch in body)
            {
                if (!inString)
                {
                    if (ch == '"')
                    {
                        inString = true;
                        current.Length = 0;
                    }
                    continue;
                }

                if (escaped)
                {
                    current.Append(ch);
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = false;
                    var value = current.ToString();
                    if (value.Length > 0)
                    {
                        result.Add(value);
                    }
                    continue;
                }

                current.Append(ch);
            }

            return result;
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}

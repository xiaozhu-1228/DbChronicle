using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

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
                    var settings = JsonSerializer.Deserialize<PluginSettings>(json);
                    if (settings != null)
                    {
                        if (settings.Disabled == null)
                        {
                            settings.Disabled = new List<string>();
                        }
                        return settings;
                    }
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

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
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
    }
}

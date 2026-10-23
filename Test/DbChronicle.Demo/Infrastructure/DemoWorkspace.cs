using System;
using System.IO;

namespace DbChronicle.Demo.Infrastructure
{
    internal sealed class DemoWorkspace
    {
        private DemoWorkspace(string root)
        {
            Root = root;
        }

        public string Root { get; private set; }

        public string MigrationsDirectory
        {
            get { return Path.Combine(Root, "migrations"); }
        }

        /// <summary>
        /// 创建演示工作区。timestamped=true 每次运行独立目录；false 固定 workspace（用于可重复的库演示）。
        /// </summary>
        public static DemoWorkspace Create(string name, bool timestamped)
        {
            var root = timestamped
                ? Path.Combine(Path.GetTempPath(), "dbchronicle-demo-" + name, DateTime.Now.ToString("yyyyMMdd_HHmmss"))
                : Path.Combine(Path.GetTempPath(), "dbchronicle-demo-" + name, "workspace");

            Directory.CreateDirectory(root);
            return new DemoWorkspace(root);
        }

        /// <summary>
        /// 随程序发布的逻辑模型目录（Test/DbChronicle.Demo/LogicModels/{scenario}，构建时复制到输出目录）。
        /// </summary>
        public static string LogicModelsDirectory(string scenario)
        {
            return Path.Combine(AppContext.BaseDirectory, "LogicModels", scenario);
        }

        /// <summary>
        /// 随程序发布的配置文件（Test/DbChronicle.Demo/Config/dbchronicle.{scenario}.yaml）。
        /// </summary>
        public static string BundledConfig(string scenario)
        {
            return Path.Combine(AppContext.BaseDirectory, "Config", "Dbchronicle." + scenario + ".xml");
        }

        public string File(string fileName)
        {
            return Path.Combine(Root, fileName);
        }

        public string DatabaseFile(string fileName)
        {
            return Path.Combine(Root, fileName);
        }

        public void EnsureMigrationsDirectory()
        {
            Directory.CreateDirectory(MigrationsDirectory);
        }
    }
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System;

using DbChronicle.Cli.Plugins;
using DbChronicle.Cli.Plugins.Loading;
using DbChronicle.Common.Enums;
using DbChronicle.Compilation.Checks;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Dialects;
using DbChronicle.Configuration;
using DbChronicle.Execution;
using DbChronicle.Migrations.Executor;
using DbChronicle.Migrations.Rollback;
using DbChronicle.Migrations.State;
using DbChronicle.Migrations.Templates;
using DbChronicle.Models;
using DbChronicle.Parsing;
using DbChronicle.Scanner;

namespace DbChronicle.Cli
{
    public static partial class Commands
    {
        public static int Scan(CliArguments args)
        {
            var from = (args.Get("from", null) ?? "").Trim().ToLowerInvariant();
            if (from == "database" || from == "db")
            {
                return ScanDatabase(args);
            }

            if (from.Length > 0 && from != "assembly" && from != "source")
            {
                throw new CliFailureException(ExitCodes.Usage,
                    "未知的 --from 取值：" + from + "（可选：source / assembly / database）");
            }

            var assemblyPath = args.Get("assembly", null);
            var sourcePath = args.Get("source", null);

            if (from == "assembly" && string.IsNullOrEmpty(assemblyPath))
            {
                throw new CliFailureException(ExitCodes.Usage, "--from assembly 需要 --assembly <dll> 参数");
            }

            if (from == "source" && string.IsNullOrEmpty(sourcePath))
            {
                throw new CliFailureException(ExitCodes.Usage, "--from source 需要 --source <dir> 参数");
            }

            if (string.IsNullOrEmpty(assemblyPath) && string.IsNullOrEmpty(sourcePath))
            {
                throw new CliFailureException(ExitCodes.Usage,
                    "缺少 --assembly / --source 参数（扫描数据库请用 --from database）");
            }

            var useAssembly = !string.IsNullOrEmpty(assemblyPath);
            if (from == "source")
            {
                useAssembly = false;
            }

            var output = ResolveOutputPath(args, "output-file", Path.Combine("migrations", "00000000_000000_baseline.xml"));

            ScanResult result;
            string sourceLabel;
            try
            {
                if (useAssembly)
                {
                    if (!File.Exists(assemblyPath))
                    {
                        throw new CliFailureException(ExitCodes.Usage, "程序集不存在：" + assemblyPath);
                    }
                    result = new DbChronicle.Scanner.CSharpScanner().ScanAssembly(Path.GetFullPath(assemblyPath));
                    sourceLabel = "C# 实体（程序集反射）";
                }
                else
                {
                    var fullSource = Path.GetFullPath(sourcePath);
                    if (!Directory.Exists(fullSource))
                    {
                        throw new CliFailureException(ExitCodes.Usage, "源码目录不存在：" + sourcePath);
                    }

                    var language = (args.Get("language", null) ?? args.Get("lang", "csharp") ?? "csharp").Trim().ToLowerInvariant();
                    if (language == "csharp" || language == "c#")
                    {
                        result = Host.ResolveSourceScanner("csharp").ScanDirectory(fullSource);
                        sourceLabel = "C# 源码（Roslyn）";
                    }
                    else
                    {
                        result = Host.ResolveSourceScanner(language).ScanDirectory(fullSource);
                        sourceLabel = language + " 源码";
                    }
                }
            }
            catch (CliFailureException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new CliFailureException(ExitCodes.Execution, "扫描失败：" + ex.Message);
            }

            if (result.Tables.Count == 0)
            {
                Console.Error.WriteLine("未识别到实体：需要 [Table] / [Entity] 特性，或类名以 Entity 结尾");
                return ExitCodes.Usage;
            }

            var xml = new BaselineXmlWriter().Generate(result, ChangeUnitFactory.ResolveAuthor(),
                DateTime.Now.ToString("yyyy-MM-dd"), sourceLabel);
            var outputPath = Path.GetFullPath(output);
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(outputPath, xml, new UTF8Encoding(false));

            foreach (var warning in result.Warnings)
            {
                Console.WriteLine("warning: " + warning);
            }

            Console.WriteLine("已生成基线草案：" + outputPath + "（" + result.Tables.Count + " 张表）");
            Console.WriteLine("提示：草案需人工确认后冻结，之后只写增量变更单元。");
            return ExitCodes.Success;
        }

        /// <summary>代码生成：把 migrations 累积出的结构模型写成各语言实体。</summary>
        public static int GenerateCode(CliArguments args)
        {
            var config = LoadConfig(args);
            var language = args.Get("language", null) ?? args.Get("lang", "csharp");
            var output = ResolveOutputPath(args, "output-dir", "Entities");
            var ns = args.Get("namespace", "Entities");

            var units = new ChangeUnitLoader(config).LoadAll();
            var model = new StructureModelBuilder().Build(units, null);
            if (model.Tables.Count == 0)
            {
                Console.Error.WriteLine("没有可生成的表：请先准备 migrations/（基线 + 增量）");
                return ExitCodes.Usage;
            }

            var options = new DbChronicle.CodeGen.EntityCodeOptions
            {
                Language = language,
                Namespace = ns,
                IncludeComments = !args.Has("no-comments"),
                IncludeColumnMapping = !args.Has("no-column-mapping")
            };

            int count;
            try
            {
                var generator = new DbChronicle.CodeGen.EntityCodeGenerator();
                count = generator.GenerateToDirectory(model, options, Path.GetFullPath(output));
            }
            catch (NotSupportedException ex)
            {
                throw new CliFailureException(ExitCodes.Usage, ex.Message);
            }

            Console.WriteLine("已生成 " + count + " 个实体文件到 " + output
                + "（语言 " + language + "，命名空间 " + ns + "，表 " + model.Tables.Count + " 张）");
            return ExitCodes.Success;
        }

        /// <summary>数据库反向扫描：读取现有库结构，生成基线草案（只读，不修改目标库）。</summary>
        public static int ScanDatabase(CliArguments args)
        {
            var config = LoadConfig(args);
            var output = ResolveOutputPath(args, "output-file", Path.Combine("migrations", "00000000_000000_baseline.xml"));
            var connection = ConnectionResolver.Resolve(config);

            var options = new ScanDatabaseOptions();
            AddList(options.Tables, args.Get("include-tables", null) ?? args.Get("tables", null));
            AddList(options.ExcludeTables, args.Get("exclude-tables", null) ?? args.Get("exclude", null));
            options.IncludeIndexes = !args.Has("no-indexes");
            options.IncludeForeignKeys = !args.Has("no-foreign-keys");
            options.IncludeCheckConstraints = !args.Has("no-check-constraints");
            options.IncludeComments = !args.Has("no-comments");

            ScanResult result;
            try
            {
                var factory = Host.ResolveExecutor(config.Database.Dialect);
                using (var executor = factory.Create(connection))
                {
                    var scanner = new DbChronicle.Scanner.Database.DatabaseScanner(executor);
                    result = scanner.Scan(options);
                    if (result.Tables.Count == 0)
                    {
                        Console.Error.WriteLine("未扫描到任何表（检查 --include-tables / --exclude-tables 与库中是否有业务表）");
                        return ExitCodes.Usage;
                    }

                    var xml = scanner.GenerateXml(result, ChangeUnitFactory.ResolveAuthor(),
                        DateTime.Now.ToString("yyyy-MM-dd"));
                    var outputPath = Path.GetFullPath(output);
                    var directory = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    File.WriteAllText(outputPath, xml, new UTF8Encoding(false));

                    foreach (var warning in result.Warnings)
                    {
                        Console.WriteLine("warning: " + warning);
                    }

                    var columnCount = 0;
                    var foreignKeyCount = 0;
                    foreach (var table in result.Tables)
                    {
                        columnCount += table.Columns.Count;
                        foreignKeyCount += table.ForeignKeys.Count;
                    }

                    Console.WriteLine("已生成基线草案：" + outputPath);
                    Console.WriteLine("  表 " + result.Tables.Count + " 张，列 " + columnCount
                        + " 个，外键 " + foreignKeyCount + " 个（方言 " + config.Database.Dialect + "）");
                    Console.WriteLine("提示：草案需人工确认后冻结，之后只写增量变更单元。");
                }
            }
            catch (CliFailureException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new CliFailureException(ExitCodes.Environment, "反向扫描失败：" + ex.Message);
            }

            return ExitCodes.Success;
        }

        private static void AddList(System.Collections.Generic.List<string> target, string csv)
        {
            if (string.IsNullOrEmpty(csv))
            {
                return;
            }
            foreach (var part in csv.Split(','))
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0)
                {
                    target.Add(trimmed);
                }
            }
        }

        /// <summary>
        /// 插件管理：<c>list</c> / <c>disable &lt;name&gt;</c> / <c>enable &lt;name&gt;</c>。
        /// 禁用状态写入用户目录（<c>%LOCALAPPDATA%\DbChronicle\settings.json</c>），
        /// 因此 <c>dotnet tool update</c> 不会恢复被禁用的插件。
        /// </summary>
    }
}
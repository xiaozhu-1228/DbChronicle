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
        public static int Compile(CliArguments args)
        {
            var config = LoadConfig(args);
            var loader = new ChangeUnitLoader(config);
            var allUnits = loader.LoadAll();
            if (allUnits.Count == 0)
            {
                throw new CliFailureException(ExitCodes.Usage, "未找到变更单元：" + loader.MigrationsDirectory);
            }

            // --pending / --change-id：只编译目标子集（增量基底仍用全量单元构建，否则 MODIFY 补全会缺上下文）
            var units = SelectUnits(args, config, allUnits);
            if (units.Count == 0)
            {
                // 已全部应用：不生成任何文件（提示已在 SelectUnits 打印）
                return ExitCodes.Success;
            }

            var allDialects = args.Has("all-dialects");
            var options = new CompilerOptions
            {
                IncrementalModify = config.Options.IncrementalModify,
                IncludeComments = config.Options.IncludeComments,
                StatementDelimiter = config.Options.StatementDelimiter
            };

            if (config.Options.IncludeRollback)
            {
                Console.WriteLine("warning W019: options.includeRollback 当前不生效，回滚请使用 db-chronicle rollback --to-release");
            }

            IReadOnlyList<string> dialects = allDialects
                ? DialectFactory.SupportedDialects
                : new[] { config.Database.Dialect };

            var outputDirectory = args.Get("out", config.ResolveOutputPath());
            Directory.CreateDirectory(outputDirectory);
            var hasError = false;

            // --split：每个（变更单元 × 方言）输出一个文件，每行一条完整语句，便于外部程序（C++ / Java 等）逐行读取执行
            var split = args.Has("split");
            if (split)
            {
                // 清单文件：外部程序按此顺序逐个执行 + 标记已应用，无需自己枚举目录
                var manifest = new StringBuilder();
                foreach (var unit in units)
                {
                    manifest.AppendLine(unit.Id + "\t" + unit.Release);
                }
                File.WriteAllText(Path.Combine(outputDirectory, "_units.txt"), manifest.ToString(), new UTF8Encoding(false));
            }

            foreach (var dialectName in dialects)
            {
                var version = allDialects ? null : config.Database.Version;
                var dialect = DialectFactory.Create(dialectName, version,
                    new DialectContext { IncrementalModify = options.IncrementalModify });

                var builder = new StringBuilder();
                var dialectFailed = false;

                foreach (var unit in units)
                {
                    // 增量模式必须用全量单元建基底（--pending 过滤掉的已应用单元同样参与推导）
                    var model = options.IncrementalModify ? new StructureModelBuilder().Build(allUnits, unit.Id) : null;
                    var result = new SchemaCompiler(dialect, options, model).Compile(unit);

                    PrintDiagnostics(result.Diagnostics);
                    if (result.HasErrors)
                    {
                        dialectFailed = true;
                        hasError = true;
                        continue;
                    }

                    if (split)
                    {
                        var unitPath = Path.Combine(outputDirectory, unit.Id + "." + dialectName + ".sql");
                        WriteStatementLines(unitPath, result.Statements);
                        Console.WriteLine("已生成：" + unitPath);
                    }
                    else
                    {
                        builder.AppendLine(result.Sql);
                    }
                }

                if (split)
                {
                    if (dialectFailed)
                    {
                        Console.Error.WriteLine("方言 " + dialectName + " 编译失败，已跳过合并输出");
                    }
                    continue;
                }

                var outputPath = Path.Combine(outputDirectory, dialectName + ".sql");
                File.WriteAllText(outputPath, builder.ToString(), new UTF8Encoding(false));
                Console.WriteLine("已生成：" + outputPath + (dialectFailed ? "（含编译错误）" : ""));
            }

            return hasError ? ExitCodes.Execution : ExitCodes.Success;
        }

        /// <summary>
        /// 选择要编译的单元：--change-id 指定单个；--pending 过滤掉状态文件里已应用的；
        /// 都未指定时保持原行为（全量）。
        /// </summary>
        private static IReadOnlyList<ChangeUnit> SelectUnits(CliArguments args, DbChronicleConfig config, IReadOnlyList<ChangeUnit> allUnits)
        {
            var changeId = args.Get("change-id", null);
            if (!string.IsNullOrEmpty(changeId))
            {
                var selected = allUnits
                    .Where(x => string.Equals(x.Id, changeId, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (selected.Count == 0)
                {
                    throw new CliFailureException(ExitCodes.Usage, "未找到变更单元：" + changeId);
                }
                return selected;
            }

            if (!args.Has("pending"))
            {
                return allUnits;
            }

            var applied = new HashSet<string>(
                new VersionStore(config).LoadAll()
                    .Where(x => !x.HasFailure)
                    .Select(x => x.ChangeId),
                StringComparer.OrdinalIgnoreCase);

            var pending = allUnits.Where(x => !applied.Contains(x.Id)).ToList();
            if (pending.Count == 0)
            {
                Console.WriteLine("没有待应用的变更单元（状态文件已是最新）");
                return pending;
            }

            Console.WriteLine("待应用 " + pending.Count + " 个变更单元：" + string.Join(", ", pending.Select(x => x.Id)));
            return pending;
        }

        /// <summary>
        /// 写成"一行一条语句"：去掉整行注释（压平后会吞掉后续内容）、合并空白、去掉末尾分号。
        /// 外部程序按行读取即可直接执行，无需自己解析 SQL 分隔符。
        /// </summary>
        private static void WriteStatementLines(string path, IEnumerable<SqlStatement> statements)
        {
            var builder = new StringBuilder();
            foreach (var statement in statements)
            {
                var line = FlattenStatement(statement.Text);
                if (line.Length > 0)
                {
                    builder.AppendLine(line);
                }
            }
            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
        }

        private static string FlattenStatement(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            var builder = new StringBuilder();
            foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("--", StringComparison.Ordinal))
                {
                    continue;
                }
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }
                builder.Append(line);
            }

            var flattened = builder.ToString().Trim();
            return flattened.EndsWith(";", StringComparison.Ordinal)
                ? flattened.Substring(0, flattened.Length - 1).TrimEnd()
                : flattened;
        }

        public static int Check(CliArguments args)
        {
            var config = LoadConfig(args);
            var units = new ChangeUnitLoader(config).LoadAll();
            var diagnostics = new List<CompileDiagnostic>();
            var compilerOptions = new CompilerOptions
            {
                IncrementalModify = config.Options.IncrementalModify,
                IncludeComments = config.Options.IncludeComments,
                StatementDelimiter = config.Options.StatementDelimiter
            };

            var dialect = DialectFactory.Create(config.Database.Dialect, config.Database.Version,
                new DialectContext { IncrementalModify = compilerOptions.IncrementalModify });

            foreach (var unit in units)
            {
                try
                {
                    var model = compilerOptions.IncrementalModify
                        ? new StructureModelBuilder().Build(units, unit.Id)
                        : null;
                    diagnostics.AddRange(new SchemaCompiler(dialect, compilerOptions, model).Compile(unit).Diagnostics);
                }
                catch (Exception ex)
                {
                    diagnostics.Add(new CompileDiagnostic
                    {
                        Code = "E043",
                        Severity = DiagnosticSeverity.Error,
                        ChangeId = unit.Id,
                        Message = "编译异常：" + ex.Message
                    });
                }
            }

            diagnostics.AddRange(new ChangeChecker().Check(units));

            PrintDiagnostics(diagnostics);

            var strict = args.Has("strict-warnings");
            var failed = diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error)
                || (strict && diagnostics.Any(x => x.Severity == DiagnosticSeverity.Warning));

            if (failed)
            {
                Console.Error.WriteLine("校验未通过（" + units.Count + " 个变更单元）");
                return ExitCodes.Usage;
            }

            Console.WriteLine("校验通过（" + units.Count + " 个变更单元）");
            return ExitCodes.Success;
        }

    }
}
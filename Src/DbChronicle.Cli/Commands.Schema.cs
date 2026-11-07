using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
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
using DbChronicle.Piping;
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

            var emit = args.Get("emit", null);
            var pipeName = args.Get("pipe", null);
            var pipeMode = !string.IsNullOrEmpty(emit) || !string.IsNullOrEmpty(pipeName);
            var progress = pipeMode ? Console.Error : Console.Out;

            // --pending / --change-id / --since：只编译目标子集（增量基底仍用全量单元构建，否则 MODIFY 补全会缺上下文）
            var units = SelectUnits(args, config, allUnits, progress);

            var allDialects = args.Has("all-dialects");
            var options = new CompilerOptions
            {
                IncrementalModify = config.Options.IncrementalModify,
                IncludeComments = config.Options.IncludeComments,
                StatementDelimiter = config.Options.StatementDelimiter
            };

            if (units.Count == 0)
            {
                // 管道模式必须给出完整流（版本记录 + 结束记录），否则调用方会判定流被截断；
                // 文件模式保持原行为（不生成任何文件）
                return pipeMode
                    ? CompileToPipe(config, allUnits, units, options, emit, pipeName, allDialects)
                    : ExitCodes.Success;
            }

            if (config.Options.IncludeRollback)
            {
                Console.Error.WriteLine("warning W019: options.includeRollback 当前不生效，回滚请使用 db-chronicle rollback --to-release");
            }

            if (pipeMode)
            {
                return CompileToPipe(config, allUnits, units, options, emit, pipeName, allDialects);
            }

            IReadOnlyList<string> dialects = allDialects
                ? DialectFactory.SupportedDialects
                : new[] { config.Database.Dialect };

            var outputDirectory = ResolveOutputPath(args, "output-dir", config.ResolveOutputPath());
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

        /// <summary>把只到天的版本（yyyyMMdd）补齐为当天第一个单元的完整 id。</summary>
        private static string NormalizeVersion(string version)
        {
            var value = (version ?? "").Trim();
            return (value.Length == 8) ? (value + "_000000") : value;
        }

        /// <summary>
        /// 选择要编译的单元：--change-id 指定单个；--since 只取版本之后的；--pending 过滤掉状态文件里已应用的；
        /// 都未指定时保持原行为（全量）。
        /// </summary>
        private static IReadOnlyList<ChangeUnit> SelectUnits(CliArguments args, DbChronicleConfig config,
            IReadOnlyList<ChangeUnit> allUnits, TextWriter progress)
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

            var since = args.Get("since", null);
            if (!string.IsNullOrEmpty(since))
            {
                var sinceValue = NormalizeVersion(since);
                var newer = allUnits
                    .Where(x => string.CompareOrdinal(x.Id, sinceValue) > 0)
                    .ToList();
                progress.WriteLine("自 " + sinceValue + " 之后共 " + newer.Count + " 个变更单元");
                return newer;
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
                progress.WriteLine("没有待应用的变更单元（状态文件已是最新）");
                return pending;
            }

            progress.WriteLine("待应用 " + pending.Count + " 个变更单元：" + string.Join(", ", pending.Select(x => x.Id)));
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

        private static int CompileToPipe(DbChronicleConfig config, IReadOnlyList<ChangeUnit> allUnits,
            IReadOnlyList<ChangeUnit> units, CompilerOptions options, string emit, string pipeName, bool allDialects)
        {
            if (!string.IsNullOrEmpty(emit) && !string.Equals(emit, "pipe", StringComparison.OrdinalIgnoreCase))
            {
                throw new CliFailureException(ExitCodes.Usage, "未知 --emit 取值：" + emit + "（当前支持 pipe）");
            }

            if (!string.IsNullOrEmpty(emit) && !string.IsNullOrEmpty(pipeName))
            {
                throw new CliFailureException(ExitCodes.Usage, "--emit pipe 与 --pipe 只能二选一");
            }

            if (allDialects)
            {
                throw new CliFailureException(ExitCodes.Usage, "管道模式只编译配置中的一种方言，不能与 --all-dialects 同用");
            }

            NamedPipeClientStream pipeClient = null;
            try
            {
                Stream target;
                if (!string.IsNullOrEmpty(pipeName))
                {
                    if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                    {
                        throw new CliFailureException(ExitCodes.Usage, "命名管道模式仅支持 Windows");
                    }

                    pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
                    try
                    {
                        pipeClient.Connect(10000);
                    }
                    catch (Exception ex)
                    {
                        throw new CliFailureException(ExitCodes.Execution,
                            "连接命名管道失败：" + pipeName + "（" + ex.Message + "）");
                    }

                    target = pipeClient;
                }
                else
                {
                    target = Console.OpenStandardOutput();
                }

                return WriteCompiledRecords(config, allUnits, units, options, target);
            }
            catch (InvalidOperationException ex)
            {
                throw new CliFailureException(ExitCodes.Execution, "管道协议错误：" + ex.Message);
            }
            catch (IOException ex)
            {
                throw new CliFailureException(ExitCodes.Execution, "管道写入失败：" + ex.Message);
            }
            finally
            {
                if (pipeClient != null)
                {
                    pipeClient.Dispose();
                }
            }
        }

        internal static int WriteCompiledRecords(DbChronicleConfig config, IReadOnlyList<ChangeUnit> allUnits,
            IReadOnlyList<ChangeUnit> units, CompilerOptions options, Stream target)
        {
            var dialect = DialectFactory.Create(config.Database.Dialect, config.Database.Version,
                new DialectContext { IncrementalModify = options.IncrementalModify });

            var writer = new PipeRecordWriter(target);
            writer.WriteVersion();

            var hasError = false;
            foreach (var unit in units)
            {
                var model = options.IncrementalModify
                    ? new StructureModelBuilder().Build(allUnits, unit.Id)
                    : null;
                var result = new SchemaCompiler(dialect, options, model).Compile(unit);

                PrintDiagnostics(result.Diagnostics, Console.Error);
                writer.WriteUnitBegin(unit.Id, unit.Release);

                if (result.HasErrors)
                {
                    hasError = true;
                    foreach (var diagnostic in result.Diagnostics)
                    {
                        if (diagnostic.Severity == DiagnosticSeverity.Error)
                        {
                            writer.WriteDiagnostic(diagnostic.Severity.ToString(), diagnostic.Code, diagnostic.Message);
                        }
                    }
                }
                else
                {
                    foreach (var statement in result.Statements)
                    {
                        var sql = NormalizePipeStatement(statement.Text);
                        if (sql.Length > 0)
                        {
                            writer.WriteStatement(sql);
                        }
                    }
                }

                writer.WriteUnitEnd();
            }

            writer.WriteStreamEnd();
            writer.Flush();
            return hasError ? ExitCodes.Execution : ExitCodes.Success;
        }

        private static string NormalizePipeStatement(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            var trimmed = text.Trim();
            return trimmed.EndsWith(";", StringComparison.Ordinal)
                ? trimmed.Substring(0, trimmed.Length - 1).TrimEnd()
                : trimmed;
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
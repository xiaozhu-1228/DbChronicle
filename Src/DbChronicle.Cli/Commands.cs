using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System;

using DbChronicle.Common.Enums;
using DbChronicle.Compilation.Checks;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Dialects;
using DbChronicle.Configuration;
using DbChronicle.Executor.AdoNet;
using DbChronicle.Migrations.Executor;
using DbChronicle.Migrations.Rollback;
using DbChronicle.Migrations.State;
using DbChronicle.Migrations.Templates;
using DbChronicle.Models;
using DbChronicle.Parsing;
using DbChronicle.Scanner;

namespace DbChronicle.Cli
{
    public static class Commands
    {
        public static int Init(CliArguments args)
        {
            var path = args.Get("config", ConfigLoader.DefaultFileName);
            if (File.Exists(path))
            {
                Console.WriteLine("配置已存在：" + path);
                return ExitCodes.Success;
            }

            var config = ConfigLoader.CreateDefault(path);

            var dialect = args.Get("dialect", null);
            if (!string.IsNullOrEmpty(dialect))
            {
                if (!DialectFactory.IsSupported(dialect))
                {
                    throw new CliFailureException(ExitCodes.Usage,
                        "不支持的方言：" + dialect + "（可选：" + string.Join(" / ", DialectFactory.SupportedDialects) + "）");
                }
                config.Database.Dialect = dialect.Trim().ToLowerInvariant();
                config.Database.Version = DefaultVersionOf(config.Database.Dialect);
            }

            var version = args.Get("version", null);
            if (!string.IsNullOrEmpty(version))
            {
                config.Database.Version = version;
            }

            ConfigLoader.Save(config, path);
            Directory.CreateDirectory(config.ResolveMigrationsPath());
            Directory.CreateDirectory(config.ResolveVersionLogPath());
            Directory.CreateDirectory(config.ResolveOutputPath());

            Console.WriteLine("已生成配置：" + path);
            Console.WriteLine("  方言：" + config.Database.Dialect + " " + config.Database.Version);
            Console.WriteLine("  migrations：" + config.ResolveMigrationsPath());
            Console.WriteLine("  version_log：" + config.ResolveVersionLogPath());
            Console.WriteLine("  output：" + config.ResolveOutputPath());
            return ExitCodes.Success;
        }

        public static int New(CliArguments args)
        {
            var config = LoadConfig(args);
            var description = args.Get("desc", null);
            if (string.IsNullOrEmpty(description))
            {
                throw new CliFailureException(ExitCodes.Usage, "缺少 --desc 参数");
            }

            var path = ChangeUnitFactory.Create(config.ResolveMigrationsPath(), description,
                args.Get("release", null), args.Get("author", null));
            Console.WriteLine(path);
            return ExitCodes.Success;
        }

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

        public static int Apply(CliArguments args)
        {
            var config = LoadConfig(args);
            var units = new ChangeUnitLoader(config).LoadAll();
            var connectionString = ResolveConnection(config);
            var options = new ApplyOptions
            {
                ConnectionString = connectionString,
                AppliedBy = ChangeUnitFactory.ResolveAuthor(),
                DryRun = args.Has("dry-run"),
                MarkAppliedChangeId = args.Get("mark-applied", null),
                Restart = args.Has("restart"),
                ContinueOnError = args.Has("continue-on-error") ? true : (bool?)null,
                LockTimeoutSeconds = config.Options.LockTimeoutSeconds,
                Log = Console.WriteLine
            };

            SchemaExecutor executor;
            try
            {
                var factory = AdoNetExecutorFactory.Resolve(config.Database.Dialect);
                executor = new SchemaExecutor(config, factory, factory);
            }
            catch (NotSupportedException ex)
            {
                throw new CliFailureException(ExitCodes.Usage, ex.Message);
            }

            if (args.Has("check-drift"))
            {
                DriftReport drift;
                try
                {
                    drift = executor.CheckDrift(units, connectionString);
                }
                catch (Exception ex)
                {
                    throw new CliFailureException(ExitCodes.Environment, "结构比对失败：" + ex.Message);
                }

                foreach (var missing in drift.MissingColumns)
                {
                    Console.Error.WriteLine("drift: 模型中有、库中没有的列：" + missing);
                }
                foreach (var extra in drift.ExtraColumns)
                {
                    Console.WriteLine("info: 库中多出的列（允许）：" + extra);
                }
                foreach (var table in drift.UnreadableTables)
                {
                    Console.WriteLine("提示: 无法读取表结构：" + table);
                }

                if (drift.HasDrift)
                {
                    Console.Error.WriteLine("发现模型漂移：请核对后再执行 apply（手工改过库时需补基线或手工对齐）");
                    return ExitCodes.Usage;
                }

                Console.WriteLine("未发现模型漂移。");
                return ExitCodes.Success;
            }

            if (args.Has("backfill-logs"))
            {
                BackfillResult backfill;
                try
                {
                    backfill = executor.BackfillLogs(units, options);
                }
                catch (Exception ex)
                {
                    throw new CliFailureException(ExitCodes.Environment, "补生成日志失败：" + ex.Message);
                }

                if (!backfill.Success)
                {
                    Console.Error.WriteLine("error: " + backfill.Message);
                    return backfill.ExitCode ?? ExitCodes.Execution;
                }

                Console.WriteLine("补生成完成：新增 " + backfill.BackfilledCount + " 个，已存在 " + backfill.SkippedCount + " 个");
                return ExitCodes.Success;
            }

            ApplyResult result;
            try
            {
                result = executor.Apply(units, options);
            }
            catch (Exception ex)
            {
                throw new CliFailureException(ExitCodes.Environment, "连接或执行环境异常：" + ex.Message);
            }

            PrintDiagnostics(result.Diagnostics);

            if (!result.Success)
            {
                Console.Error.WriteLine("error: " + result.Message);
                return result.ExitCode ?? ExitCodes.Execution;
            }

            Console.WriteLine("完成：应用 " + result.AppliedCount + " 个，跳过 " + result.SkippedCount + " 个");
            return ExitCodes.Success;
        }

        public static int Status(CliArguments args)
        {
            var config = LoadConfig(args);
            var units = new ChangeUnitLoader(config).LoadAll();
            var applied = LoadApplied(config);

            var releaseFilter = args.Get("release", null);
            var current = applied
                .Where(x => !x.HasFailure)
                .Select(x => x.Release)
                .Where(x => !string.IsNullOrEmpty(x))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .LastOrDefault();

            var appliedIds = new HashSet<string>(applied.Where(x => !x.HasFailure).Select(x => x.ChangeId), StringComparer.OrdinalIgnoreCase);
            var pending = units.Where(x => !appliedIds.Contains(x.Id)
                && (releaseFilter == null || x.Release == releaseFilter)).ToList();
            var pendingRelease = pending
                .Select(x => x.Release)
                .Where(x => !string.IsNullOrEmpty(x))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .LastOrDefault();

            Console.WriteLine(releaseFilter == null
                ? "数据库当前版本：" + (current ?? "无（未初始化）")
                : "数据库当前版本（过滤 release=" + releaseFilter + "）：" + (current ?? "无（未初始化）"));

            if (pending.Count > 0)
            {
                Console.WriteLine("待应用版本：" + (pendingRelease ?? "无"));
            }

            Console.WriteLine();

            Console.WriteLine("已应用：");
            foreach (var record in applied.Where(x => releaseFilter == null || x.Release == releaseFilter))
            {
                Console.WriteLine((record.HasFailure ? "[!] " : "[x] ") + record.ChangeId + "  " + record.Release
                    + "  " + (record.HasFailure ? "failed at stmt " + record.FailureStatement : "applied " + record.AppliedAt)
                    + " by " + record.AppliedBy);
            }

            Console.WriteLine();
            Console.WriteLine("待应用：");
            foreach (var unit in pending)
            {
                Console.WriteLine("[ ] " + unit.Id + "  " + unit.Release + "  pending  " + (unit.Description ?? ""));
            }

            if (pending.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("提示：执行 db-chronicle apply 应用 " + pending.Count + " 个待应用的变更单元。");
                return ExitCodes.Pending;
            }

            Console.WriteLine();
            Console.WriteLine("数据库已是最新版本。");
            return ExitCodes.Success;
        }

        public static int Verify(CliArguments args)
        {
            var config = LoadConfig(args);
            var units = new ChangeUnitLoader(config).LoadAll();
            var applied = LoadApplied(config);

            var map = new Dictionary<string, ChangeUnit>(StringComparer.OrdinalIgnoreCase);
            foreach (var unit in units)
            {
                map[unit.Id] = unit;
            }

            var errors = 0;

            foreach (var record in applied)
            {
                ChangeUnit unit;
                if (!map.TryGetValue(record.ChangeId, out unit))
                {
                    Console.Error.WriteLine("error: 已应用的变更单元文件丢失：" + record.ChangeId);
                    errors++;
                    continue;
                }

                if (string.Equals(record.Checksum, unit.Checksum, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("[x] " + record.ChangeId);
                    continue;
                }

                var normalized = ChecksumUtil.ComputeNormalizedFile(unit.SourcePath);
                if (string.Equals(record.Checksum, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("[?] " + record.ChangeId + "  疑似仅换行或编码变动（规范化后一致）");
                    continue;
                }

                Console.Error.WriteLine("error: 已应用的变更单元内容被修改：" + record.ChangeId
                    + "（已应用=" + record.Checksum + "，当前=" + unit.Checksum + "）");
                errors++;
            }

            if (errors > 0)
            {
                return ExitCodes.Execution;
            }

            Console.WriteLine("checksum 校验通过（" + applied.Count + " 条记录）。");
            return ExitCodes.Success;
        }

        public static int Scan(CliArguments args)
        {
            var assemblyPath = args.Get("assembly", null);
            var sourcePath = args.Get("source", null);
            if (string.IsNullOrEmpty(assemblyPath) && string.IsNullOrEmpty(sourcePath))
            {
                throw new CliFailureException(ExitCodes.Usage, "缺少 --assembly 或 --source 参数");
            }

            var output = args.Get("out", Path.Combine("migrations", "00000000_000000_baseline.xml"));

            ScanResult result;
            string sourceLabel;
            try
            {
                if (!string.IsNullOrEmpty(assemblyPath))
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

                    var language = (args.Get("lang", "csharp") ?? "csharp").Trim().ToLowerInvariant();
                    if (language == "csharp" || language == "c#")
                    {
                        result = new DbChronicle.Scanner.Source.SourceScanner().ScanDirectory(fullSource);
                        sourceLabel = "C# 源码（Roslyn）";
                    }
                    else
                    {
                        result = new DbChronicle.Scanner.Languages.LanguageSourceScanner().ScanDirectory(fullSource, language);
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
            var language = args.Get("lang", "csharp");
            var output = args.Get("out", "Entities");
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
            var output = args.Get("out", Path.Combine("migrations", "00000000_000000_baseline.xml"));
            var connection = ConnectionResolver.Resolve(config);

            var options = new ScanDatabaseOptions();
            AddList(options.Tables, args.Get("tables", null));
            AddList(options.ExcludeTables, args.Get("exclude", null));
            options.IncludeIndexes = !args.Has("no-indexes");
            options.IncludeForeignKeys = !args.Has("no-foreign-keys");
            options.IncludeCheckConstraints = !args.Has("no-check-constraints");
            options.IncludeComments = !args.Has("no-comments");

            ScanResult result;
            try
            {
                var factory = new AdoNetExecutorFactory(config.Database.Dialect);
                using (var executor = factory.Create(connection))
                {
                    var scanner = new DbChronicle.Scanner.Database.DatabaseScanner(executor);
                    result = scanner.Scan(options);
                    if (result.Tables.Count == 0)
                    {
                        Console.Error.WriteLine("未扫描到任何表（检查 --tables / --exclude 与库中是否有业务表）");
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

        public static int Rollback(CliArguments args)
        {
            var config = LoadConfig(args);
            var target = args.Get("to-release", null);
            if (string.IsNullOrEmpty(target))
            {
                throw new CliFailureException(ExitCodes.Usage, "缺少 --to-release 参数");
            }

            var units = new ChangeUnitLoader(config).LoadAll();
            AdoNetExecutorFactory factory;
            try
            {
                factory = AdoNetExecutorFactory.Resolve(config.Database.Dialect);
            }
            catch (NotSupportedException ex)
            {
                throw new CliFailureException(ExitCodes.Usage, ex.Message);
            }

            var service = new RollbackService(config, factory, factory);
            RollbackPlan plan;
            try
            {
                plan = service.Plan(units, target);
            }
            catch (Exception ex)
            {
                throw new CliFailureException(ExitCodes.Execution, "生成回滚计划失败：" + ex.Message);
            }

            if (plan.Steps.Count == 0)
            {
                Console.WriteLine("没有需要回滚的变更单元（目标版本：" + target + "）");
                return ExitCodes.Success;
            }

            Console.Write(plan.ToSql());

            if (plan.HasBlockers)
            {
                foreach (var step in plan.Steps)
                {
                    foreach (var blocker in step.Blockers)
                    {
                        Console.Error.WriteLine("blocked: " + step.Unit.Id + "：" + blocker);
                    }
                }
                Console.Error.WriteLine("存在无法自动回滚的操作，已中止（请手工处理）");
                return ExitCodes.Usage;
            }

            if (!args.Has("apply"))
            {
                Console.WriteLine("（dry-run：确认无误后加 --apply 执行）");
                return ExitCodes.Success;
            }

            var connectionString = ResolveConnection(config);
            RollbackResult result;
            try
            {
                result = service.Execute(plan, connectionString);
            }
            catch (Exception ex)
            {
                throw new CliFailureException(ExitCodes.Environment, "回滚执行异常：" + ex.Message);
            }

            if (!result.Success)
            {
                foreach (var blocker in result.Blockers)
                {
                    Console.Error.WriteLine("blocked: " + blocker);
                }
                Console.Error.WriteLine("error: " + result.Message);
                return result.ExitCode ?? ExitCodes.Execution;
            }

            Console.WriteLine("回滚完成：撤销 " + result.RolledBackCount + " 个变更单元");
            return ExitCodes.Success;
        }

        private static string DefaultVersionOf(string dialect)
        {
            switch (dialect)
            {
                case "mariadb":
                    return "10.5";
                case "postgresql":
                    return "14";
                case "sqlserver":
                    return "2019";
                case "sqlite":
                    return "3.35";
                default:
                    return "8.0";
            }
        }

        private static List<AppliedChange> LoadApplied(DbChronicleConfig config)
        {
            try
            {
                return new VersionStore(config).LoadAll();
            }
            catch (Exception ex)
            {
                throw new CliFailureException(ExitCodes.Execution, "读取状态文件失败：" + ex.Message);
            }
        }

        private static string ResolveConnection(DbChronicleConfig config)
        {
            try
            {
                return ConnectionResolver.Resolve(config);
            }
            catch (InvalidOperationException ex)
            {
                throw new CliFailureException(ExitCodes.Environment, ex.Message);
            }
        }

        private static DbChronicleConfig LoadConfig(CliArguments args)
        {
            var explicitPath = args.Has("config");
            var path = args.Get("config", ConfigLoader.DefaultFileName);
            if (File.Exists(path))
            {
                return ConfigLoader.Load(path);
            }

            if (explicitPath)
            {
                throw new CliFailureException(ExitCodes.Usage,
                    "配置文件不存在：" + Path.GetFullPath(path) + "（先执行 db-chronicle init）");
            }

            var created = ConfigLoader.CreateDefault(path);
            ConfigLoader.Save(created, path);
            Console.WriteLine("未找到配置文件，已生成默认配置：" + created.ConfigPath + "（可用 db-chronicle init --dialect <name> 调整方言）");
            return created;
        }

        private static void PrintDiagnostics(IEnumerable<CompileDiagnostic> diagnostics)
        {
            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic.Severity == DiagnosticSeverity.Error)
                {
                    Console.Error.WriteLine(diagnostic.ToString());
                }
                else
                {
                    Console.WriteLine(diagnostic.ToString());
                }
            }
        }
    }
}
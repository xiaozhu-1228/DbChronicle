using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System;

using DbChronicle.Common.Enums;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Compilation.Dialects;
using DbChronicle.Configuration;
using DbChronicle.Demo.Infrastructure;
using DbChronicle.Demo.OperationTests.Cases;
using DbChronicle.Executor.AdoNet;
using DbChronicle.Migrations.Executor;
using DbChronicle.Models;
using DbChronicle.Parsing;

namespace DbChronicle.Demo.OperationTests
{
    internal sealed class OperationTestRunner
    {
        private static readonly Regex VersionPattern = new Regex(@"^\d+(\.\d+){0,2}", RegexOptions.Compiled);

        private readonly ConsoleReporter _console;
        private readonly DbChronicleConfig _baseConfig;

        public OperationTestRunner(ConsoleReporter console, DbChronicleConfig baseConfig)
        {
            _console = console;
            _baseConfig = baseConfig;
        }

        public int Run(string[] args)
        {
            var options = OpsTestOptions.Parse(args, _baseConfig.Database.Dialect);
            var dialect = options.Dialect;

            if (!AdoNetExecutorFactory.IsSupported(dialect))
            {
                _console.Line("不支持的操作测试方言：" + dialect + "（支持 mysql / postgresql / sqlite）");
                return 1;
            }

            var cases = OperationCaseCatalog.Create(dialect);
            var workRoot = Path.Combine(Path.GetTempPath(), "dbchronicle-ops",
                dialect + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(workRoot);

            string connectionString;
            if (dialect == "sqlite")
            {
                connectionString = TestDatabase.Prepare(dialect, null,
                    options.Database ?? OpsTestOptions.DefaultDatabaseName(dialect),
                    TestDatabase.ResolveSqlitePath(Path.Combine(workRoot, "database")));
            }
            else
            {
                var baseConnectionString = dialect == "mysql"
                    ? DemoConnectionStrings.ResolveMySql()
                    : DemoConnectionStrings.ResolvePostgreSql();
                connectionString = TestDatabase.Prepare(dialect, baseConnectionString,
                    options.Database ?? OpsTestOptions.DefaultDatabaseName(dialect), null);
            }

            var factory = AdoNetExecutorFactory.Resolve(dialect);
            string version;
            string database;
            using (var executor = factory.Create(connectionString))
            {
                version = NormalizeVersion(TestDatabase.ReadVersion(dialect, executor));
                database = ReadDatabaseName(dialect, executor);
            }

            _console.Line("============================================================");
            _console.Line("DbChronicle 13 类操作测试");
            _console.Line("方言：" + dialect + " " + version);
            _console.Line("测试库：" + database + (TestDatabase.UsedFallback ? "（回退到原连接串）" : "（专用库）"));
            _console.Line("模型目录：" + OperationAssets.Root);
            _console.Line("临时目录：" + workRoot + "（全部通过后自动删除，--keep 保留）");
            _console.Line("============================================================");

            var results = new List<CaseResult>();
            var stopwatch = Stopwatch.StartNew();
            foreach (var testCase in cases)
            {
                if (!options.Matches(testCase.CaseId))
                {
                    continue;
                }

                var result = RunCase(testCase, dialect, version, connectionString, workRoot);
                results.Add(result);
                PrintCase(result);
            }

            stopwatch.Stop();
            PrintSummary(results, stopwatch.Elapsed);

            var allPassed = results.Count > 0 && results.All(x => x.Passed);
            if (allPassed && !options.KeepObjects)
            {
                try
                {
                    Directory.Delete(workRoot, true);
                }
                catch
                {
                }
            }
            else if (!allPassed)
            {
                _console.Line("失败用例的临时目录已保留，可用 --keep 保留全部：" + workRoot);
            }

            return allPassed ? 0 : 1;
        }

        private CaseResult RunCase(OperationTestCase testCase, string dialect, string version,
            string connectionString, string workRoot)
        {
            var result = new CaseResult
            {
                CaseId = testCase.CaseId,
                Title = testCase.Title,
                FileName = testCase.FileName
            };

            var caseDirectory = Path.Combine(workRoot, testCase.CaseId);
            var migrationsDirectory = Path.Combine(caseDirectory, "migrations");
            Directory.CreateDirectory(migrationsDirectory);

            var source = OperationAssets.Resolve(dialect, testCase.FileName);
            if (!File.Exists(source))
            {
                result.FatalError = "未找到逻辑模型文件：" + source;
                return result;
            }

            File.Copy(source, Path.Combine(migrationsDirectory, testCase.FileName), true);
            WriteBaseline(testCase, migrationsDirectory);
            var config = CreateConfig(caseDirectory, dialect, version, testCase.IncrementalModify);
            var factory = AdoNetExecutorFactory.Resolve(dialect);

            try
            {
                using (var executor = factory.Create(connectionString))
                {
                    var context = new OpsTestContext(executor, new SchemaProbe(executor, dialect),
                        dialect, TestDatabase.DatabaseName, result.Checks)
                    {
                        Version = version
                    };

                    if (testCase.Cleanup != null)
                    {
                        try
                        {
                            testCase.Cleanup(context);
                        }
                        catch (Exception ex)
                        {
                            _console.Line("  （清理跳过：" + ex.Message + "）");
                        }
                    }

                    if (testCase.Setup != null)
                    {
                        testCase.Setup(context);
                    }

                    var units = new ChangeUnitLoader(config).LoadAll();
                    var unit = units.FirstOrDefault(x => string.Equals(
                            Path.GetFileName(x.SourcePath ?? ""), testCase.FileName, StringComparison.OrdinalIgnoreCase))
                        ?? units.LastOrDefault();
                    if (unit == null)
                    {
                        result.FatalError = "逻辑模型文件未解析出变更单元";
                        return result;
                    }

                    var dialectImpl = DialectFactory.Create(dialect, version,
                        new DialectContext { IncrementalModify = config.Options.IncrementalModify });
                    var model = config.Options.IncrementalModify
                        ? new StructureModelBuilder().Build(units, unit.Id)
                        : null;
                    var compiled = new SchemaCompiler(dialectImpl,
                        new CompilerOptions
                        {
                            IncrementalModify = config.Options.IncrementalModify,
                            IncludeComments = true
                        }, model)
                        .Compile(unit);
                    result.CompiledSql = compiled.Sql;
                    context.CompiledSql = compiled.Sql;
                    context.Diagnostics = compiled.Diagnostics;

                    if (testCase.ExpectCompileError)
                    {
                        VerifyCompileError(testCase, compiled, result);
                        return result;
                    }

                    AddCompileDiagnostics(compiled, result);

                    if (compiled.HasErrors)
                    {
                        result.FatalError = "编译失败：" + string.Join("；", compiled.Diagnostics
                            .Where(x => x.Severity == DiagnosticSeverity.Error)
                            .Select(x => x.Code + " " + x.Message));
                        return result;
                    }

                    VerifyWarnings(testCase, compiled, result);

                    if (testCase.CompileOnly)
                    {
                        testCase.Verify(context);
                        return result;
                    }

                    var applyResult = new SchemaExecutor(config, factory, null).Apply(
                        new ChangeUnitLoader(config).LoadAll(),
                        new ApplyOptions
                        {
                            ConnectionString = connectionString,
                            AppliedBy = "ops-test",
                            ContinueOnError = true,
                            LockTimeoutSeconds = 0
                        });

                    if (!applyResult.Success)
                    {
                        var detail = applyResult.Message ?? "";
                        if (applyResult.Failures.Count > 0)
                        {
                            detail += " | " + string.Join(" ; ", applyResult.Failures);
                        }

                        if (testCase.ExpectExecutionError || IsExpectedExecutionError(testCase, version))
                        {
                            if (!string.IsNullOrEmpty(testCase.ExpectedExecutionErrorContains)
                                && detail.IndexOf(testCase.ExpectedExecutionErrorContains, StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                result.FatalError = "执行失败，但原因不是预期的能力边界（缺关键字 "
                                    + testCase.ExpectedExecutionErrorContains + "）：" + detail;
                                return result;
                            }

                            result.Checks.Add(new CheckResult
                            {
                                Id = "预期失败",
                                Title = testCase.ExpectExecutionError
                                    ? "用例故意制造冲突/失败，按预期接受执行失败"
                                    : "低于 " + testCase.ExpectedExecutionErrorVersionBelow
                                        + " 的服务器不支持该语句，按版本能力边界接受执行失败",
                                Outcome = CheckOutcome.Pass,
                                Detail = detail
                            });
                        }
                        else
                        {
                            result.FatalError = "执行失败：" + detail;
                            return result;
                        }
                    }

                    testCase.Verify(context);
                }
            }
            catch (Exception ex)
            {
                result.FatalError = (result.FatalError == null ? "" : result.FatalError + " ") + "异常：" + ex.Message;
            }

            return result;
        }

        private static void WriteBaseline(OperationTestCase testCase, string migrationsDirectory)
        {
            if (string.IsNullOrEmpty(testCase.BaselineXml))
            {
                return;
            }

            var xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
                + "<changeUnit id=\"00000000_000000_baseline\" author=\"ops-test\" release=\"00000000\">\n"
                + "  <description>baseline for " + testCase.CaseId + "</description>\n"
                + testCase.BaselineXml
                + "\n</changeUnit>\n";
            File.WriteAllText(Path.Combine(migrationsDirectory, "00000000_000000_baseline.xml"),
                xml, new System.Text.UTF8Encoding(false));
        }

        private static void VerifyCompileError(OperationTestCase testCase, CompileResult compiled, CaseResult result)
        {
            var errors = compiled.Diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error).ToList();
            var matched = errors.Any(x => string.Equals(x.Code, testCase.ExpectedErrorCode, StringComparison.OrdinalIgnoreCase));
            var detail = errors.Count == 0
                ? "没有任何错误诊断"
                : string.Join("；", errors.Select(x => x.Code + " " + x.Message));

            result.Checks.Add(new CheckResult
            {
                Id = testCase.ExpectedErrorCode,
                Title = "方言能力边界：应拒绝编译",
                Outcome = matched ? CheckOutcome.Pass : CheckOutcome.Fail,
                Detail = "错误诊断=" + detail
            });
        }

        private static void VerifyWarnings(OperationTestCase testCase, CompileResult compiled, CaseResult result)
        {
            if (testCase.ExpectedWarnings == null)
            {
                return;
            }

            foreach (var code in testCase.ExpectedWarnings)
            {
                var found = compiled.Diagnostics.Any(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
                result.Checks.Add(new CheckResult
                {
                    Id = code,
                    Title = "期望出现诊断 " + code,
                    Outcome = found ? CheckOutcome.Pass : CheckOutcome.Fail,
                    Detail = found ? null : "未出现该诊断"
                });
            }
        }

        private static void AddCompileDiagnostics(CompileResult compiled, CaseResult result)
        {
            foreach (var diagnostic in compiled.Diagnostics)
            {
                var outcome = diagnostic.Severity == DiagnosticSeverity.Error
                    ? CheckOutcome.Fail
                    : CheckOutcome.Warning;
                result.Checks.Add(new CheckResult
                {
                    Id = diagnostic.Code,
                    Title = "编译诊断",
                    Outcome = outcome,
                    Detail = diagnostic.Message
                });
            }
        }

        private static DbChronicleConfig CreateConfig(string caseDirectory, string dialect, string version, bool incrementalModify)
        {
            var config = new DbChronicleConfig { BaseDirectory = caseDirectory };
            config.Database.Dialect = dialect;
            config.Database.Version = version;
            config.Environment = dialect;
            config.Paths.Migrations = "./migrations";
            config.Paths.VersionLog = "./version_log/${environment}";
            config.Paths.Output = "./output";
            config.Options.AllowDestructive = true;
            config.Options.IncrementalModify = incrementalModify;
            config.Options.IncludeComments = true;
            config.Options.CommitScope = "unit";
            config.Options.LockTimeoutSeconds = 0;
            return config;
        }

        private void PrintCase(CaseResult result)
        {
            var status = result.Passed ? "PASS" : "FAIL";
            _console.Line("");
            _console.Line("[" + result.CaseId + "] " + result.Title + "  " + status
                + "（检查 " + result.Checks.Count + "：通过 " + result.PassCount + "，失败 " + result.FailCount
                + "，警告 " + result.WarningCount + "，缺口 " + result.GapCount + "，跳过 " + result.SkipCount + "）");

            if (!string.IsNullOrEmpty(result.FatalError))
            {
                _console.Line("  FATAL " + result.FatalError);
            }

            foreach (var check in result.Checks)
            {
                var line = "  " + Prefix(check.Outcome) + " " + check.Id + " " + check.Title;
                if (!string.IsNullOrEmpty(check.Detail))
                {
                    line += " | " + check.Detail;
                }

                if (!string.IsNullOrEmpty(check.Reason))
                {
                    line += " | " + check.Reason;
                }

                _console.Line(line);
            }

            if (!result.Passed && !string.IsNullOrEmpty(result.CompiledSql))
            {
                var sql = result.CompiledSql.Replace("\r", " ").Replace("\n", " ").Trim();
                if (sql.Length > 900)
                {
                    sql = sql.Substring(0, 900) + " ...";
                }

                _console.Line("  SQL  " + sql);
            }
        }

        private void PrintSummary(IReadOnlyList<CaseResult> results, TimeSpan elapsed)
        {
            _console.Line("");
            _console.Line("============================================================");

            var pass = results.Count(x => x.Passed);
            var checks = results.Sum(x => x.Checks.Count);
            var failedChecks = results.Sum(x => x.FailCount);
            var warnings = results.Sum(x => x.WarningCount);
            var gaps = results.Sum(x => x.GapCount);
            var skips = results.Sum(x => x.SkipCount);

            _console.Line("汇总：" + results.Count + " 类操作，用例通过 " + pass + "/" + results.Count
                + "，检查项 " + checks + "，失败 " + failedChecks + "，警告 " + warnings
                + "，已知缺口 " + gaps + "，跳过 " + skips);
            _console.Line("耗时：" + elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + " 秒");

            var failed = results.Where(x => !x.Passed).ToList();
            if (failed.Count > 0)
            {
                _console.Line("");
                _console.Line("未通过用例：");
                foreach (var item in failed)
                {
                    _console.Line("  [" + item.CaseId + "] " + item.Title
                        + (string.IsNullOrEmpty(item.FatalError) ? "" : " | " + item.FatalError));
                }
            }

            var gapItems = results.SelectMany(x => x.Checks
                .Where(c => c.Outcome == CheckOutcome.KnownGap)
                .Select(c => "  [" + x.CaseId + "] " + c.Id + " " + c.Title + " | " + c.Reason)).ToList();
            if (gapItems.Count > 0)
            {
                _console.Line("");
                _console.Line("已知能力缺口（实现未支持，测试如实标注）：");
                foreach (var item in gapItems)
                {
                    _console.Line(item);
                }
            }

            _console.Line("============================================================");
        }

        private static string Prefix(CheckOutcome outcome)
        {
            switch (outcome)
            {
                case CheckOutcome.Pass:
                    return "PASS";

                case CheckOutcome.Fail:
                    return "FAIL";

                case CheckOutcome.Warning:
                    return "WARN";

                case CheckOutcome.Skip:
                    return "SKIP";

                default:
                    return "GAP ";
            }
        }

        private static bool IsExpectedExecutionError(OperationTestCase testCase, string version)
        {
            if (string.IsNullOrEmpty(testCase.ExpectedExecutionErrorVersionBelow))
            {
                return false;
            }

            System.Version current;
            System.Version threshold;
            if (!System.Version.TryParse(version ?? "", out current)
                || !System.Version.TryParse(testCase.ExpectedExecutionErrorVersionBelow, out threshold))
            {
                return false;
            }

            return current < threshold;
        }

        private static string NormalizeVersion(string version)
        {
            var match = VersionPattern.Match((version ?? "").Trim());
            return match.Success ? match.Value : (version ?? "").Trim();
        }

        private static string ReadDatabaseName(string dialect, DbChronicle.Execution.IDbExecutor executor)
        {
            switch (dialect)
            {
                case "mysql":
                    return Convert.ToString(executor.Scalar("SELECT DATABASE()"));

                case "postgresql":
                    return Convert.ToString(executor.Scalar("SELECT current_database()"));

                default:
                    return TestDatabase.DatabaseName;
            }
        }
    }
}

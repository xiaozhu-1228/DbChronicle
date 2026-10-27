using System;

namespace DbChronicle.Cli
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
            }
            catch
            {
            }

            if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
            {
                PrintUsage();
                return args.Length == 0 ? ExitCodes.Usage : ExitCodes.Success;
            }

            var command = args[0].ToLowerInvariant();
            var arguments = CliArguments.Parse(Slice(args, 1));

            try
            {
                switch (command)
                {
                    case "init":
                        return Commands.Init(arguments);
                    case "new":
                        return Commands.New(arguments);
                    case "compile":
                        return Commands.Compile(arguments);
                    case "check":
                        return Commands.Check(arguments);
                    case "upgrade":
                    case "apply":
                        return Commands.Apply(arguments);
                    case "status":
                        return Commands.Status(arguments);
                    case "verify":
                        return Commands.Verify(arguments);
                    case "scan":
                        return Commands.Scan(arguments);
                    case "scan-db":
                        return Commands.ScanDatabase(arguments);
                    case "codegen":
                        return Commands.GenerateCode(arguments);
                    case "rollback":
                        return Commands.Rollback(arguments);
                    default:
                        Console.Error.WriteLine("未知命令：" + command);
                        PrintUsage();
                        return ExitCodes.Usage;
                }
            }
            catch (CliFailureException ex)
            {
                Console.Error.WriteLine("error: " + ex.Message);
                return ex.ExitCode;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("error: " + ex.Message);
                return ExitCodes.Execution;
            }
        }

        private static string[] Slice(string[] args, int start)
        {
            var result = new string[Math.Max(0, args.Length - start)];
            Array.Copy(args, start, result, 0, result.Length);
            return result;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("db-chronicle —— 数据库版本托管与升级工具集");
            Console.WriteLine();
            Console.WriteLine("用法：db-chronicle <command> [options]");
            Console.WriteLine();
            Console.WriteLine("  init                  生成默认配置与目录（--dialect <mysql|mariadb|postgresql|sqlserver|sqlite>）");
            Console.WriteLine("  new --desc <text>     生成变更单元（可选 --release / --author）");
            Console.WriteLine("  compile               编译为 SQL（不连数据库）");
            Console.WriteLine("  check                 检测语义冲突（不连数据库）");
            Console.WriteLine("  upgrade               执行待应用的变更单元，写入状态与审计（别名 apply）");
            Console.WriteLine("                        （--dry-run / --restart / --check-drift /");
            Console.WriteLine("                        --continue-on-error / --mark-applied <id> / --backfill-logs）");
            Console.WriteLine("  status                查看版本托管状态（读本地状态文件，--release <yyyyMMdd>）");
            Console.WriteLine("  verify                校验已应用文件的 checksum（读本地状态文件）");
            Console.WriteLine("  scan --assembly <dll> 从 C# 实体生成基线草案（--out <file>）");
            Console.WriteLine("  scan --source <dir>   从源码生成基线草案（--lang csharp|java|go|typescript|auto，默认 csharp）");
            Console.WriteLine("  scan-db               从现有数据库反向生成基线草案（--out <file> / --tables a,b / --exclude c）");
            Console.WriteLine("  codegen               由 migrations 生成实体代码（--lang csharp|java|go|typescript|cpp --out <dir> --namespace <ns>）");
            Console.WriteLine("  rollback --to-release <yyyyMMdd>  生成回滚 SQL（默认 dry-run，加 --apply 执行）");
            Console.WriteLine();
            Console.WriteLine("通用选项：--config <path>（默认 DbchronicleConfig.xml，缺失时自动生成）");
            Console.WriteLine("连接信息：环境变量 DBCHRONICLE_CONN，或交互式输入");
        }
    }
}

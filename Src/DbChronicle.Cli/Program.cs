using System;

namespace DbChronicle.Cli
{
    public static class ExitCodes
    {
        public const int Success = 0;
        public const int Usage = 1;
        public const int Pending = 2;
        public const int Execution = 3;
        public const int Locked = 4;
        public const int Environment = 5;
    }

    public sealed class CliFailureException : Exception
    {
        public CliFailureException(int exitCode, string message)
            : base(message)
        {
            ExitCode = exitCode;
        }

        public int ExitCode { get; private set; }
    }

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
                    case "apply":
                        return Commands.Apply(arguments);
                    case "status":
                        return Commands.Status(arguments);
                    case "verify":
                        return Commands.Verify(arguments);
                    case "scan":
                        return Commands.Scan(arguments);
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
            Console.WriteLine("  apply                 应用到数据库（--dry-run / --restart / --check-drift /");
            Console.WriteLine("                        --mark-applied <id> / --backfill-logs）");
            Console.WriteLine("  status                查看版本托管状态（--release <yyyyMMdd>）");
            Console.WriteLine("  verify                校验已应用文件的 checksum");
            Console.WriteLine("  scan --assembly <dll> 从 C# 实体生成基线草案（--out <file>）");
            Console.WriteLine("  rollback --to-release <yyyyMMdd>  生成回滚 SQL（默认 dry-run，加 --apply 执行）");
            Console.WriteLine();
            Console.WriteLine("通用选项：--config <path>（默认 dbchronicle.config.yaml）");
            Console.WriteLine("连接信息：环境变量 DBCHRONICLE_CONN，或交互式输入");
        }
    }
}

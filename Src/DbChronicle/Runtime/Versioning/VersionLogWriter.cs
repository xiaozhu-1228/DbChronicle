using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DbChronicle.Abstractions.Diagnostics;
using DbChronicle.Compilation.Compiler;
using DbChronicle.Domain.Model;

namespace DbChronicle.Runtime.Versioning
{
    public sealed class VersionLogWriter
    {
        private readonly string _directory;

        public VersionLogWriter(string versionLogDirectory)
        {
            _directory = versionLogDirectory;
        }

        public string WriteSqlSnapshot(ChangeUnit unit, CompileResult result, string dialectName)
        {
            if (string.IsNullOrEmpty(_directory))
            {
                return null;
            }

            var directory = Path.Combine(_directory, "sql", unit.Release ?? "unknown");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, unit.Id + "." + dialectName + ".sql");
            File.WriteAllText(path, result.Sql, new UTF8Encoding(false));
            return path;
        }

        public string AppendEntry(ChangeUnit unit, CompileResult result, string appliedBy, string database, string sqlSnapshotPath, string dialectName, bool backfilled)
        {
            if (string.IsNullOrEmpty(_directory))
            {
                return null;
            }

            Directory.CreateDirectory(_directory);
            var fileName = (unit.Release ?? "unknown") + "_dbupdate.md";
            var path = Path.Combine(_directory, fileName);

            var sb = new StringBuilder();
            sb.Append("## ").AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " +08:00");
            sb.AppendLine();
            sb.Append("- **执行人**：").AppendLine(string.IsNullOrEmpty(appliedBy) ? "unknown" : appliedBy);
            sb.Append("- **数据库**：").AppendLine(database);
            sb.AppendLine("- **变更单元**：");
            sb.Append("  - `").Append(unit.Id).Append("` ").AppendLine(unit.Description ?? "");
            sb.AppendLine("- **执行结果**：成功");

            var warnings = CollectWarnings(result);
            sb.Append("- **警告**：").AppendLine(warnings.Count == 0 ? "无" : string.Join("；", warnings));

            if (backfilled)
            {
                sb.AppendLine("- **备注**：按当前方言重新编译补生成，可能与当时执行的语句存在方言版本差异");
            }

            if (!string.IsNullOrEmpty(sqlSnapshotPath))
            {
                sb.Append("- **SQL 快照**：`").Append(RelativeDirectory(unit.Release)).Append('/')
                  .Append(unit.Id).Append('.').Append(dialectName).Append(".sql`").AppendLine();
            }

            sb.AppendLine("- **SQL 脚本**：");
            sb.AppendLine();
            sb.AppendLine("```sql");
            sb.AppendLine(result.Sql.TrimEnd());
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();

            var exists = File.Exists(path);
            if (!exists)
            {
                var header = "# " + (unit.Release ?? "unknown") + " 数据库版本更新日志" + Environment.NewLine + Environment.NewLine;
                File.AppendAllText(path, header, new UTF8Encoding(false));
            }

            File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
            return path;
        }

        private static string RelativeDirectory(string release)
        {
            return "sql/" + (release ?? "unknown");
        }

        private static List<string> CollectWarnings(CompileResult result)
        {
            var list = new List<string>();
            foreach (var diagnostic in result.Diagnostics)
            {
                if (diagnostic.Severity == DiagnosticSeverity.Warning)
                {
                    list.Add(diagnostic.ToString());
                }
            }
            return list;
        }
    }
}

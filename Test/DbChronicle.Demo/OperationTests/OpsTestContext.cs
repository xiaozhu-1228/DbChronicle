using System.Collections.Generic;
using System.Globalization;
using System;

using DbChronicle.Execution;
using DbChronicle.Models;

namespace DbChronicle.Demo.OperationTests
{
    internal sealed class OpsTestContext
    {
        private readonly List<CheckResult> _checks;

        public OpsTestContext(IDbExecutor executor, SchemaProbe probe, string dialect, string database, List<CheckResult> checks)
        {
            Executor = executor;
            Probe = probe;
            Dialect = dialect;
            Database = database;
            _checks = checks;
        }

        public IDbExecutor Executor { get; private set; }

        public SchemaProbe Probe { get; private set; }

        public string Dialect { get; private set; }

        public string Database { get; private set; }

        public string Version { get; set; }

        public string CompiledSql { get; set; }

        public IReadOnlyList<CompileDiagnostic> Diagnostics { get; set; }

        public IReadOnlyList<CheckResult> Checks
        {
            get { return _checks; }
        }

        public void Execute(string sql)
        {
            Executor.Execute(sql);
        }

        public void Setup(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
            {
                return;
            }

            Executor.Execute(sql);
        }

        public void Pass(string id, string title, string detail = null)
        {
            Add(id, title, CheckOutcome.Pass, detail);
        }

        public void Fail(string id, string title, string detail)
        {
            Add(id, title, CheckOutcome.Fail, detail);
        }

        public void Warn(string id, string title, string detail)
        {
            Add(id, title, CheckOutcome.Warning, detail);
        }

        public void Skip(string id, string title, string detail)
        {
            Add(id, title, CheckOutcome.Skip, detail);
        }

        public void Gap(string id, string title, string reason)
        {
            var check = new CheckResult
            {
                Id = id,
                Title = title,
                Outcome = CheckOutcome.KnownGap,
                Reason = reason
            };
            _checks.Add(check);
        }

        public void Check(string id, string title, bool condition, string detail = null)
        {
            if (condition)
            {
                Pass(id, title, detail);
            }
            else
            {
                Fail(id, title, detail);
            }
        }

        public void Equal(string id, string title, object expected, object actual)
        {
            var expectedText = ToText(expected);
            var actualText = ToText(actual);
            Check(id, title, string.Equals(expectedText, actualText, StringComparison.OrdinalIgnoreCase),
                "期望=" + expectedText + "，实际=" + actualText);
        }

        public void Contains(string id, string title, string expectedPart, string actual)
        {
            var text = actual ?? "";
            Check(id, title, text.ToLowerInvariant().Contains((expectedPart ?? "").ToLowerInvariant()),
                "期望包含=" + expectedPart + "，实际=" + text);
        }

        public void StartsWith(string id, string title, string expectedPrefix, string actual)
        {
            var text = actual ?? "";
            Check(id, title, text.StartsWith(expectedPrefix ?? "", StringComparison.OrdinalIgnoreCase),
                "期望前缀=" + expectedPrefix + "，实际=" + text);
        }

        public bool VersionBelow(string threshold)
        {
            System.Version current;
            System.Version target;
            if (!System.Version.TryParse(Version ?? "", out current) || !System.Version.TryParse(threshold, out target))
            {
                return false;
            }

            return current < target;
        }

        public static string ToText(object value)
        {
            if (value == null)
            {
                return "<null>";
            }

            if (value is bool flag)
            {
                return flag ? "true" : "false";
            }

            if (value is byte[] bytes)
            {
                return "0x" + BitConverter.ToString(bytes).Replace("-", "");
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private void Add(string id, string title, CheckOutcome outcome, string detail)
        {
            _checks.Add(new CheckResult
            {
                Id = id,
                Title = title,
                Outcome = outcome,
                Detail = detail
            });
        }
    }
}

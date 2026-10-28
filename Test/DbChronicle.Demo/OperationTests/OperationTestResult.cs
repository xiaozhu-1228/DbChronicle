using System.Collections.Generic;

namespace DbChronicle.Demo.OperationTests
{
    internal enum CheckOutcome
    {
        Pass,
        Fail,
        Warning,
        Skip,
        KnownGap
    }

    internal sealed class CheckResult
    {
        public string Id { get; set; }

        public string Title { get; set; }

        public CheckOutcome Outcome { get; set; }

        public string Detail { get; set; }

        public string Reason { get; set; }
    }

    internal sealed class CaseResult
    {
        public string CaseId { get; set; }

        public string Title { get; set; }

        public string FileName { get; set; }

        public List<CheckResult> Checks { get; private set; } = new List<CheckResult>();

        public string FatalError { get; set; }

        public string ErrorCode { get; set; }

        public string CompiledSql { get; set; }

        public int PassCount
        {
            get { return Count(CheckOutcome.Pass); }
        }

        public int FailCount
        {
            get { return Count(CheckOutcome.Fail); }
        }

        public int WarningCount
        {
            get { return Count(CheckOutcome.Warning); }
        }

        public int SkipCount
        {
            get { return Count(CheckOutcome.Skip); }
        }

        public int GapCount
        {
            get { return Count(CheckOutcome.KnownGap); }
        }

        public bool Passed
        {
            get { return string.IsNullOrEmpty(FatalError) && FailCount == 0; }
        }

        private int Count(CheckOutcome outcome)
        {
            var total = 0;
            foreach (var check in Checks)
            {
                if (check.Outcome == outcome)
                {
                    total++;
                }
            }

            return total;
        }
    }
}

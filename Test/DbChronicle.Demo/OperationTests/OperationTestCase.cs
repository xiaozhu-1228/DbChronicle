using System;

namespace DbChronicle.Demo.OperationTests
{
    internal sealed class OperationTestCase
    {
        public string CaseId { get; set; }

        public string Title { get; set; }

        public string FileName { get; set; }

        public string BaselineXml { get; set; }

        public bool IncrementalModify { get; set; }

        public Action<OpsTestContext> Cleanup { get; set; }

        public Action<OpsTestContext> Setup { get; set; }

        public Action<OpsTestContext> Verify { get; set; }

        public bool ExpectCompileError { get; set; }

        public string ExpectedErrorCode { get; set; }

        public bool CompileOnly { get; set; }

        public string[] ExpectedWarnings { get; set; }

        public string ExpectedExecutionErrorVersionBelow { get; set; }

        public string ExpectedExecutionErrorContains { get; set; }

        public bool ExpectExecutionError { get; set; }
    }
}

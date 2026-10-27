using System;

namespace DbChronicle.Cli
{
    public sealed class CliFailureException : Exception
    {
        public CliFailureException(int exitCode, string message)
            : base(message)
        {
            ExitCode = exitCode;
        }

        public int ExitCode { get; private set; }
    }
}

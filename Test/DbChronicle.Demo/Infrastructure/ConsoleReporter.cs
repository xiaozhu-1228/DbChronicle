using System;
using System.IO;

namespace DbChronicle.Demo.Infrastructure
{
    internal sealed class ConsoleReporter
    {
        private readonly TextWriter _output;

        public ConsoleReporter(TextWriter output)
        {
            _output = output;
        }

        public static ConsoleReporter Console { get; } = new ConsoleReporter(System.Console.Out);

        public void Line(string message)
        {
            _output.WriteLine(message);
        }
    }
}
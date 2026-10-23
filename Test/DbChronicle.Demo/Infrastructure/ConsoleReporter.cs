using System;
using System.IO;

namespace DbChronicle.Demo.Infrastructure
{
    internal sealed class ConsoleReporter
    {
        private readonly TextWriter _output;
        private readonly TextWriter _error;

        public ConsoleReporter(TextWriter output, TextWriter error)
        {
            _output = output;
            _error = error;
        }

        public static ConsoleReporter Console { get; } = new ConsoleReporter(System.Console.Out, System.Console.Error);

        public Action<string> Log
        {
            get { return _output.WriteLine; }
        }

        public void Section(string title)
        {
            _output.WriteLine();
            _output.WriteLine("=== " + title + " ===");
        }

        public void Line(string message)
        {
            _output.WriteLine(message);
        }

        public void Line()
        {
            _output.WriteLine();
        }

        public void Items(string prefix, string message)
        {
            _output.WriteLine(prefix + message);
        }

        public void Error(string message)
        {
            _error.WriteLine(message);
        }

        public static string Short(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }
            return value.Length <= 12 ? value : value.Substring(0, 12);
        }
    }
}

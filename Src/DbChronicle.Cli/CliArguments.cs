using System;
using System.Collections.Generic;

namespace DbChronicle.Cli
{
    public sealed class CliArguments
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public List<string> Positionals { get; } = new List<string>();

        public static CliArguments Parse(string[] args)
        {
            var result = new CliArguments();
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg.StartsWith("--", StringComparison.Ordinal))
                {
                    var name = arg.Substring(2);
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        result._values[name] = args[i + 1];
                        i++;
                    }
                    else
                    {
                        result._values[name] = "true";
                    }
                }
                else if (arg.StartsWith("-", StringComparison.Ordinal) && arg.Length > 1)
                {
                    result._values[arg.Substring(1)] = "true";
                }
                else
                {
                    result.Positionals.Add(arg);
                }
            }
            return result;
        }

        public bool Has(string name)
        {
            return _values.ContainsKey(name);
        }

        public string Get(string name, string fallback)
        {
            string value;
            if (_values.TryGetValue(name, out value))
            {
                return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ? fallback : value;
            }
            return fallback;
        }

        public int GetInt(string name, int fallback)
        {
            int value;
            var text = Get(name, null);
            return text != null && int.TryParse(text, out value) ? value : fallback;
        }
    }
}

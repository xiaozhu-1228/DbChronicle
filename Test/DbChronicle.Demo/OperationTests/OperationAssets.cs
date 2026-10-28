using System.IO;
using System;

namespace DbChronicle.Demo.OperationTests
{
    internal static class OperationAssets
    {
        public static string Root { get; } = Locate();

        public static string Resolve(string dialect, string fileName)
        {
            return Path.Combine(Root, dialect, fileName);
        }

        private static string Locate()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 8 && directory != null; i++)
            {
                var candidate = Path.Combine(directory.FullName, "LogicModels", "Ops");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            return Path.Combine(AppContext.BaseDirectory, "LogicModels", "Ops");
        }
    }
}

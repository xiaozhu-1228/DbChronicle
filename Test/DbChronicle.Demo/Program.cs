using System;
using System.Text;
using DbChronicle.Demo.Cli;

namespace DbChronicle.Demo
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
                DemoRunner.Run(DemoOptions.Parse(args));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("demo 失败：" + ex);
                return 1;
            }
        }
    }
}

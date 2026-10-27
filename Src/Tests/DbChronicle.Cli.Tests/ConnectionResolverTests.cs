using System;

using DbChronicle.Configuration;
using Xunit;

namespace DbChronicle.Cli.Tests
{
    public class ConnectionResolverTests
    {
        private static DbChronicleConfig CreateConfig()
        {
            return new DbChronicleConfig
            {
                BaseDirectory = System.IO.Path.GetTempPath(),
                Database = new DatabaseSettings { Dialect = "mysql" }
            };
        }

        [Fact]
        public void Resolve_ShouldPreferEnvironmentVariable()
        {
            var previous = Environment.GetEnvironmentVariable(ConnectionResolver.EnvironmentVariable);
            try
            {
                Environment.SetEnvironmentVariable(ConnectionResolver.EnvironmentVariable, "Server=env;Database=db");
                var interactive = false;

                var result = ConnectionResolver.Resolve(CreateConfig(), () => interactive, _ => "Server=prompt");

                Assert.Equal("Server=env;Database=db", result);
            }
            finally
            {
                Environment.SetEnvironmentVariable(ConnectionResolver.EnvironmentVariable, previous);
            }
        }

        [Fact]
        public void Resolve_ShouldThrow_WhenNonInteractiveWithoutEnvironment()
        {
            var previous = Environment.GetEnvironmentVariable(ConnectionResolver.EnvironmentVariable);
            try
            {
                Environment.SetEnvironmentVariable(ConnectionResolver.EnvironmentVariable, null);

                var exception = Assert.Throws<InvalidOperationException>(() =>
                    ConnectionResolver.Resolve(CreateConfig(), () => true, _ => "Server=prompt"));

                Assert.Contains(ConnectionResolver.EnvironmentVariable, exception.Message);
            }
            finally
            {
                Environment.SetEnvironmentVariable(ConnectionResolver.EnvironmentVariable, previous);
            }
        }

        [Fact]
        public void Resolve_ShouldPrompt_WhenInteractiveWithoutEnvironment()
        {
            var previous = Environment.GetEnvironmentVariable(ConnectionResolver.EnvironmentVariable);
            try
            {
                Environment.SetEnvironmentVariable(ConnectionResolver.EnvironmentVariable, null);

                var result = ConnectionResolver.Resolve(CreateConfig(), () => false, _ => "Server=prompt;Database=db");

                Assert.Equal("Server=prompt;Database=db", result);
            }
            finally
            {
                Environment.SetEnvironmentVariable(ConnectionResolver.EnvironmentVariable, previous);
            }
        }
    }
}
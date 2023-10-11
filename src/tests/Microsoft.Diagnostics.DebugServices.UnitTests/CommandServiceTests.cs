using Microsoft.Diagnostics.DebugServices.Implementation;
using Microsoft.Diagnostics.TestHelpers;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using Xunit.Extensions;

[assembly: SuppressMessage("Performance", "CA1825:Avoid zero-length array allocations.", Justification = "<Pending>")]

namespace Microsoft.Diagnostics.DebugServices.UnitTests
{
    public class CommandServiceTests : IDisposable
    {
        private const string ListenerName = "CommandServiceTests";

        private static IEnumerable<object[]> _configurations;

        /// <summary>
        /// Get the first test asset dump. It doesn't matter which one.
        /// </summary>
        /// <returns></returns>
        public static IEnumerable<object[]> GetConfiguration()
        {
            return _configurations ??= TestRunConfiguration.Instance.Configurations
                .Where((config) => config.AllSettings.ContainsKey("DumpFile"))
                .Take(1)
                .Select(c => new[] { c })
                .ToImmutableArray();
        }

        ITestOutputHelper Output { get; set; }

        public CommandServiceTests(ITestOutputHelper output)
        {
            Output = output;
            LoggingListener.EnableListener(output, ListenerName);
        }

        void IDisposable.Dispose() => Trace.Listeners.Remove(ListenerName);

        [SkippableTheory, MemberData(nameof(GetConfiguration))]
        public void CommandServiceTest1(TestConfiguration config)
        {
            using TestDump testDump = new(config);

            CaptureConsoleService consoleService = new();
            testDump.ServiceContainer.AddService<IConsoleService>(consoleService);

            CommandService commandService = new();
            testDump.ServiceContainer.AddService<ICommandService>(commandService);

            // Add all the test commands
            commandService.AddCommands(typeof(TestCommand1).Assembly);

            // See if the test commands exists
            Assert.Contains(commandService.Commands, ((string name, string help, IEnumerable<string> aliases) cmd) => cmd.name == "testcommand");

            // Invoke only TestCommand1
            TestCommand1.FilterValue = true;
            TestCommand1.Invoked = false;
            TestCommand2.FilterValue = false;
            TestCommand2.Invoked = false;
            TestCommand3.FilterValue = false;
            TestCommand3.Invoked = false;
            Assert.True(commandService.Execute("testcommand", testDump.Target.Services));
            Assert.True(TestCommand1.Invoked);
            Assert.False(TestCommand2.Invoked);
            Assert.False(TestCommand3.Invoked);

            // Check for TestCommand1 help
            string help1 = commandService.GetDetailedHelp("testcommand", testDump.Target.Services, consoleWidth: int.MaxValue);
            Assert.NotNull(help1);
            Output.WriteLine(help1);
            Assert.Contains("Test command #1", help1);

            // Invoke only TestCommand2
            TestCommand1.FilterValue = false;
            TestCommand1.Invoked = false;
            TestCommand2.FilterValue = true;
            TestCommand2.Invoked = false;
            TestCommand3.FilterValue = false;
            TestCommand3.Invoked = false;
            Assert.True(commandService.Execute("testcommand", testDump.Target.Services));
            Assert.False(TestCommand1.Invoked);
            Assert.True(TestCommand2.Invoked);
            Assert.False(TestCommand3.Invoked);

            // Invoke only TestCommand3

            TestCommand1.FilterValue = false;
            TestCommand1.Invoked = false;
            TestCommand2.FilterValue = false;
            TestCommand2.Invoked = false;
            TestCommand3.FilterValue = true;
            TestCommand3.Invoked = false;
            Assert.True(commandService.Execute("testcommand", "--foo 23", testDump.Target.Services));
            Assert.False(TestCommand1.Invoked);
            Assert.False(TestCommand2.Invoked);
            Assert.True(TestCommand3.Invoked);

            // Check for TestCommand3 help
            string help3 = commandService.GetDetailedHelp("testcommand", testDump.Target.Services, consoleWidth: int.MaxValue);
            Assert.NotNull(help3);
            Output.WriteLine(help3);
            Assert.Contains("Test command #3", help3);

            // Invoke none of the test commands
            TestCommand1.FilterValue = false;
            TestCommand1.Invoked = false;
            TestCommand2.FilterValue = false;
            TestCommand2.Invoked = false;
            TestCommand3.FilterValue = false;
            TestCommand3.Invoked = false;
            try
            {
                Assert.False(commandService.Execute("testcommand", testDump.Target.Services));
            }
            catch (DiagnosticsException ex)
            {
                Assert.Matches("Test command #2 filter", ex.Message);
            }
            Assert.False(TestCommand1.Invoked);
            Assert.False(TestCommand2.Invoked);
            Assert.False(TestCommand3.Invoked);
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Monitoring;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Diagnostics.Monitoring.UnitTests
{
    public class CommandLineHelperTests
    {
        private readonly ITestOutputHelper _output;

        public CommandLineHelperTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Theory]
        [InlineData(true, null, null)]
        [InlineData(true, "", "")]
        [InlineData(true, @"C:\NoArgs\test.exe", @"C:\NoArgs\test.exe")]
        [InlineData(true, @"C:\WithArgs\test.exe arg1 arg2", @"C:\WithArgs\test.exe")]
        [InlineData(true, @"""C:\With Space No Args\test.exe""", @"C:\With Space No Args\test.exe")]
        [InlineData(true, @"""C:\With Space With Args\test.exe"" arg1 arg2", @"C:\With Space With Args\test.exe")]
        [InlineData(true, @"C:\With'Quotes'No'Args\test.exe", @"C:\With'Quotes'No'Args\test.exe")]
        [InlineData(true, @"C:\With'Quotes'With'Args\test.exe arg1 arg2", @"C:\With'Quotes'With'Args\test.exe")]
        [InlineData(false, null, null)]
        [InlineData(false, "", "")]
        [InlineData(false, "/home/noargs/test", "/home/noargs/test")]
        [InlineData(false, "/home/withargs/test arg1 arg2", "/home/withargs/test")]
        [InlineData(false, @"""/home/with space no args/test""", "/home/with space no args/test")]
        [InlineData(false, @"""/home/with space with args/test"" arg1 arg2", "/home/with space with args/test")]
        [InlineData(false, @"""/home/escaped\\backslashes\\no\\args/test""", @"/home/escaped\backslashes\no\args/test")]
        [InlineData(false, @"""/home/escaped\\backslashes\\with\\args/test"" arg1 arg2", @"/home/escaped\backslashes\with\args/test")]
        [InlineData(false, @"""/home/escaped\""quotes\""no\""args/test""", @"/home/escaped""quotes""no""args/test")]
        [InlineData(false, @"""/home/escaped\""quotes\""with\""args/test"" arg1 arg2", @"/home/escaped""quotes""with""args/test")]
        public void CommandLineValidPathTest(bool isWindows, string commandLine, string expectedProcessPath)
        {
            Assert.Equal(expectedProcessPath, CommandLineHelper.ExtractExecutablePath(commandLine, isWindows));
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using Microsoft.Diagnostics.DebugServices;
using Xunit;

namespace Microsoft.Diagnostics.ExtensionCommands.UnitTests
{
    public class SetClrPathTests
    {
        [Theory]
        [InlineData(@"\\remote.example\share")]
        [InlineData("//remote.example/share")]
        [InlineData(@"\\?\UNC\remote.example\share")]
        public void SetClrPathRejectsRemoteDirectory(string path)
        {
            TestRuntime runtime = new();
            SetClrPath command = new() { Console = new NullConsole(), Runtime = runtime, Argument = path };

            Assert.Throws<DiagnosticsException>(command.Invoke);
            Assert.Null(runtime.RuntimeModuleDirectory);
        }

        [Fact]
        public void SetClrPathAcceptsLocalDirectory()
        {
            TestRuntime runtime = new();
            string directory = System.IO.Path.GetTempPath();
            SetClrPath command = new() { Console = new NullConsole(), Runtime = runtime, Argument = directory };

            command.Invoke();

            Assert.Equal(System.IO.Path.GetFullPath(directory), runtime.RuntimeModuleDirectory);
        }

        private sealed class NullConsole : IConsoleService
        {
            public bool SupportsDml => false;
            public int WindowWidth => 80;
            public CancellationToken CancellationToken { get; set; }
            public void WriteString(OutputType type, OutputLevel level, string text) { }
        }

        private sealed class TestRuntime : IRuntime
        {
            public int Id => 0;
            public ITarget Target => throw new NotImplementedException();
            public IServiceProvider Services => throw new NotImplementedException();
            public RuntimeType RuntimeType => RuntimeType.NetCore;
            public Version RuntimeVersion => throw new NotImplementedException();
            public IModule RuntimeModule => throw new NotImplementedException();
            public string RuntimeModuleDirectory { get; set; }
            public string GetDacFilePath(out bool verifySignature) => throw new NotImplementedException();
            public int GetClrDataProcessFromCDac(out IntPtr clrDataProcess) => throw new NotImplementedException();
            public string GetDbiFilePath() => throw new NotImplementedException();
        }
    }
}

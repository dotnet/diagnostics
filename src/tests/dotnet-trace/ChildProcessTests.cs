// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using System;
using Xunit;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Diagnostics.Tools.Trace
{
    public class ChildProcessTests
    {
        private readonly ITestOutputHelper output;

        public ChildProcessTests(ITestOutputHelper outputHelper)
        {
            output = outputHelper;
        }

        [Theory]
        [InlineData("1000", 1000)]
        [InlineData("0", 0)]
        public void VerifyExitCode(string commandLineArg, int exitCode)
        {
            using TestRunner runner(CommonHelper.GetTraceePathWithArgs(traceeName:"dotnet-trace", output);
            runner.Start();

        }
    }
}
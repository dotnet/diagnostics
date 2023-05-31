// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Diagnostics.CommonTestRunner;
using Microsoft.Diagnostics.TestHelpers;
using Xunit;
using Xunit.Abstractions;
using Xunit.Extensions;
using TestRunner = Microsoft.Diagnostics.CommonTestRunner.TestRunner;

// Newer SDKs flag MemberData(nameof(Configurations)) with this error
// Avoid unnecessary zero-length array allocations.  Use Array.Empty<object>() instead.
#pragma warning disable CA1825

namespace Microsoft.Diagnostics.NETCore.Client
{
    public class PerfMapTests
    {
        private readonly ITestOutputHelper _output;

        public static IEnumerable<object[]> Configurations => TestRunner.Configurations;

        public PerfMapTests(ITestOutputHelper outputHelper)
        {
            _output = outputHelper;
        }

        [SkippableTheory, MemberData(nameof(Configurations))]
        public async Task EnableAll(TestConfiguration config)
        {
            if (OS.Kind != OSKind.Linux)
            {
                throw new SkipTestException($"Not supported on {OS.Kind}");
            }

            await using TestRunner runner = await TestRunner.Create(config, _output, "Tracee");
            await runner.Start(testProcessTimeout: 60_000);
            DiagnosticsClientApiShim clientShim = new(new DiagnosticsClient(runner.Pid), useAsync);
        }
    }
}

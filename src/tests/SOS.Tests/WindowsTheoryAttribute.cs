// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using SOS.TestHarness;
using Xunit.Sdk;
using Xunit.v3;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// A <see cref="TheoryAttribute"/> for an SOS test whose matrix is populated only on Windows — for example
/// a cdb-only matrix (<c>!clru</c>, <c>!dumpstack</c>/<c>!eestack</c>), which <c>TestConfig.IsValid</c>
/// gates to the Windows-only cdb host. Non-Windows targets produce no test cases. Imported bundles
/// may also exclude native-debugger or live rows; ordinary local runs still fail on unexpected empty data.
/// </summary>
[XunitTestCaseDiscoverer(typeof(WindowsTheoryDiscoverer))]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute(
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        SkipTestWithoutData = DumpStore.IsReadOnly;
    }
}

public sealed class WindowsTheoryDiscoverer : TheoryDiscoverer
{
    public override ValueTask<IReadOnlyCollection<IXunitTestCase>> Discover(
        ITestFrameworkDiscoveryOptions discoveryOptions,
        IXunitTestMethod testMethod,
        IFactAttribute factAttribute)
    {
        if (!DumpStore.IsReadOnly && !OperatingSystem.IsWindows())
        {
            return ValueTask.FromResult<IReadOnlyCollection<IXunitTestCase>>(Array.Empty<IXunitTestCase>());
        }

        return base.Discover(discoveryOptions, testMethod, factAttribute);
    }
}

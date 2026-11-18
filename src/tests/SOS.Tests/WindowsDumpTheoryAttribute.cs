// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Xunit.Sdk;
using Xunit.v3;

namespace SOS.Tests;

[XunitTestCaseDiscoverer(typeof(WindowsDumpTheoryDiscoverer))]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class WindowsDumpTheoryAttribute : SosDumpTheoryAttribute
{
    public WindowsDumpTheoryAttribute(
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
    }
}

public sealed class WindowsDumpTheoryDiscoverer : SosDumpTheoryDiscoverer
{
    public override ValueTask<IReadOnlyCollection<IXunitTestCase>> Discover(
        ITestFrameworkDiscoveryOptions discoveryOptions,
        IXunitTestMethod testMethod,
        IFactAttribute factAttribute) =>
        OperatingSystem.IsWindows()
            ? base.Discover(discoveryOptions, testMethod, factAttribute)
            : ValueTask.FromResult<IReadOnlyCollection<IXunitTestCase>>([]);
}

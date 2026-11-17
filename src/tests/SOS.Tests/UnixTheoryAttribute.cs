// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit.Sdk;
using Xunit.v3;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// A <see cref="TheoryAttribute"/> for an SOS test whose matrix is populated only on Linux and macOS -- for
/// example an LLDB-only matrix, which <c>TestConfig.IsValid</c> removes on Windows. On Windows the matrix
/// is legitimately empty, so the attribute discovers no test cases. Elsewhere, an unexpectedly empty matrix
/// remains a real failure.
/// </summary>
[XunitTestCaseDiscoverer(typeof(UnixTheoryDiscoverer))]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class UnixTheoryAttribute : TheoryAttribute
{
    public UnixTheoryAttribute(
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
    }
}

public sealed class UnixTheoryDiscoverer : TheoryDiscoverer
{
    public override ValueTask<IReadOnlyCollection<IXunitTestCase>> Discover(
        ITestFrameworkDiscoveryOptions discoveryOptions,
        IXunitTestMethod testMethod,
        IFactAttribute factAttribute)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ValueTask.FromResult<IReadOnlyCollection<IXunitTestCase>>(Array.Empty<IXunitTestCase>());
        }

        return base.Discover(discoveryOptions, testMethod, factAttribute);
    }
}

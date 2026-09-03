// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// A <see cref="TheoryAttribute"/> for a cross-platform SOS test — one whose matrix is non-empty on every
/// platform the suite runs on. Every SOS test sources its data from <c>TestConfig.BuildMatrix</c>, which
/// filters out configurations that don't apply to the current platform. xUnit's default behaviour (an
/// empty data set is a failure) is kept deliberately: if such a matrix comes back empty it means the test
/// was misconfigured (or a host regressed), and that should surface as a real failure rather than a silent
/// skip. Tests whose matrix is intentionally single-platform use a platform-gated attribute instead, for
/// example <see cref="WindowsTheoryAttribute"/> for cdb-only matrices.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SosTheoryAttribute : TheoryAttribute
{
    public SosTheoryAttribute(
        [System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "",
        [System.Runtime.CompilerServices.CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        SkipTestWithoutData = TestConfig.AllowEmptyMatrix(Environment.GetEnvironmentVariable);
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit.v3;

namespace SOS.TestHarness;

/// <summary>
/// Thrown by the harness to turn a structural host/flavor limitation into an xUnit <em>dynamic skip</em>
/// (rather than a failure) from inside harness code, where the per-test <c>Assert.Skip</c> APIs are not
/// available. xUnit v3 recognizes a skip by the <see cref="DynamicSkipToken.Value"/> prefix on the
/// exception message, so any test that lets this exception propagate is reported as skipped with
/// <paramref name="reason"/>.
///
/// Use this only for limitations that are intrinsic to a (host, flavor, liveness) combination and apply
/// uniformly to every test exercising it — i.e. there is no per-test variation to express.
/// </summary>
public sealed class HarnessSkipException : Exception
{
    public HarnessSkipException(string reason)
        : base(DynamicSkipToken.Value + reason)
    {
    }

    /// <summary>Throw a <see cref="HarnessSkipException"/> to skip the current test with <paramref name="reason"/>.</summary>
    public static void Now(string reason) => throw new HarnessSkipException(reason);
}

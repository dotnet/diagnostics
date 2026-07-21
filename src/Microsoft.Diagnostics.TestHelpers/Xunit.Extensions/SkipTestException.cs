// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Xunit.Extensions
{
    /// <summary>
    /// Exception that dynamically skips a test under xUnit v3. The message is prefixed
    /// with the dynamic-skip token so the xUnit v3 runner reports the test as skipped
    /// (the same mechanism used by <c>Assert.Skip</c>) instead of failed.
    /// </summary>
    public class SkipTestException : Exception
    {
        // Mirrors the internal Xunit.Sdk.DynamicSkipToken.Value constant from xunit.v3.assert.
        private const string DynamicSkipToken = "$XunitDynamicSkip$";

        public SkipTestException(string reason)
            : base(DynamicSkipToken + reason) { }
    }
}

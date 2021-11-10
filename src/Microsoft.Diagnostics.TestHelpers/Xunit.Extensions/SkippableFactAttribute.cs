// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit;
using Xunit.Sdk;

namespace Xunit.Extensions
{
    [XunitTestCaseDiscoverer("Xunit.Extensions.SkippableFactDiscoverer", "Microsoft.Diagnostics.TestHelpers")]
    public class SkippableFactAttribute : FactAttribute { }
}

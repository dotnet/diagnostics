// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics.Tracing;

namespace Microsoft.Diagnostics.Tools.RuntimeClient.Tests
{
    internal sealed class TestProvider
    {
        public ulong Keywords { get; set; }

        public EventLevel EventLevel { get; set; }

        public string Name { get; set; }

        public string FilterData { get; set; }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.ExtensionCommands
{
    public class DumpGenStats
    {
        public ClrType Type { get; set; }
        public ulong NumberOfOccurences { get; set; }
        public ulong TotalSize { get; set; }
    }
}

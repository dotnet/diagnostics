// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.Implementation
{
    internal sealed class ClrExtendedModuleData
    {
        public string? SimpleName { get; set; }
        public string? FileName { get; set; }
        public bool IsFlatLayout { get; set; }
        public bool IsDynamic { get; set; }
        public ulong Size { get; set; }
    }
}
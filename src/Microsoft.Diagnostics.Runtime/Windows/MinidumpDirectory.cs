// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.Windows
{
    internal readonly struct MinidumpDirectory
    {
        public readonly MinidumpStreamType StreamType;
        public readonly uint DataSize;
        public readonly uint Rva;
    }
}

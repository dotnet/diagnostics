// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Runtime.MacOS.Structs
{
    internal readonly struct DyldImageInfo
    {
        public UIntPtr ImageLoadAddress { get; }
        public UIntPtr ImageFilePath { get; }
        public UIntPtr ImageFileModDate { get; }
    }
}

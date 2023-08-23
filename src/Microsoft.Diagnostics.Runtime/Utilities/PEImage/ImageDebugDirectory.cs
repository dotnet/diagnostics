// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    internal struct ImageDebugDirectory
    {
        public int Characteristics;
        public int TimeDateStamp;
        public short MajorVersion;
        public short MinorVersion;
        public ImageDebugType Type;
        public int SizeOfData;
        public int AddressOfRawData;
        public int PointerToRawData;
    }
}
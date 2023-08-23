// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    /// <summary>
    /// Each resource data entry describes a leaf node in the resource directory
    /// tree.  It contains an offset, relative to the beginning of the resource
    /// directory of the data for the resource, a size field that gives the number
    /// of bytes of data at that offset, a CodePage that should be used when
    /// decoding code point values within the resource data.  Typically for new
    /// applications the code page would be the Unicode code page.
    /// </summary>
    internal struct ImageResourceDataEntry
    {
        public int RvaToData;
        public int Size;
        public int CodePage;
        public int Reserved;
    }
}
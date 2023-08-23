// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    /// <summary>
    /// Resource directory consists of two counts, following by a variable length
    /// array of directory entries.  The first count is the number of entries at
    /// beginning of the array that have actual names associated with each entry.
    /// The entries are in ascending order, case insensitive strings.  The second
    /// count is the number of entries that immediately follow the named entries.
    /// This second count identifies the number of entries that have 16-bit integer
    /// Ids as their name.  These entries are also sorted in ascending order.
    /// This structure allows fast lookup by either name or number, but for any
    /// given resource entry only one form of lookup is supported, not both.
    /// </summary>
    internal struct IMAGE_RESOURCE_DIRECTORY
    {
        public int Characteristics;
        public int TimeDateStamp;
        public short MajorVersion;
        public short MinorVersion;
        public ushort NumberOfNamedEntries;
        public ushort NumberOfIdEntries;
        // IMAGE_RESOURCE_DIRECTORY_ENTRY DirectoryEntries[];
    }
}

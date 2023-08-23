// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// Provides auxillary information about a coredump or Windows minidump.
    ///
    /// This interface is not used by the ClrMD library itself, but is here to provide extra
    /// information and functionality to some tools consuming ClrMD.  You do not need to implement
    /// this interface when implementing IDataReader unless you are handing it to a tool which
    /// requires it.
    ///
    /// This inteface must always be requested and not assumed to be there:
    ///
    ///     IDataReader reader = ...;
    ///
    ///     if (reader is IDumpInfoProvider dumpInfoProvider)
    ///         ...
    /// </summary>
    public interface IDumpInfoProvider
    {
        /// <summary>
        /// Returns whether the dump is a mini or triage dump (that is, full heap information was
        /// explicitly NOT placed into the dump).
        /// </summary>
        bool IsMiniOrTriage { get; }
    }
}
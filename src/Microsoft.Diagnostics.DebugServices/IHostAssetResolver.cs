// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Answers questions about where the host's assets live — the native binaries the host ships
    /// (the native sos module, the cDAC, DiaSymReader, …). The directory of those assets is
    /// host-specific: a native debugger host supplies it (the SOS hosting layer feeds it from the
    /// host's sos module location), while in-process hosts (dotnet-dump) derive it from the tool's
    /// package layout. Runtimes and other services query this resolver instead of reasoning about
    /// layouts themselves.
    /// </summary>
    public interface IHostAssetResolver
    {
        /// <summary>
        /// The directory containing the host's native binaries (the native sos module and the
        /// cDAC that ships next to it).
        /// </summary>
        string NativeBinariesDirectory { get; }

        /// <summary>
        /// The full path to where the cDAC native library (mscordaccore_universal) ships for the
        /// current host (next to the native sos module). The path is not probed; the caller is
        /// expected to check existence (the cDAC is not bundled in, for example, release builds).
        /// </summary>
        string GetCDacPath();
    }
}

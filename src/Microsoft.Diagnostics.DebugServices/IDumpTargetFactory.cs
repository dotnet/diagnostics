// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Opens and creates minidump and core dump targets.
    /// </summary>
    public interface IDumpTargetFactory
    {
        /// <summary>
        /// Opens and creates a dump data target.
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns>target instance</returns>
        /// <exception cref="DiagnosticsException">can not construct target instance</exception>
        /// <exception cref="NotSupportedException">target platform not supported</exception>
        ITarget OpenDump(string fileName);
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Opens and creates minidump and coredump targets.
    /// </summary>
    public interface IDumpTargetFactory
    {
        /// <summary>
        /// Opens and creates a dump data target.
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns>target instance</returns>
        ITarget OpenDump(string fileName);
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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

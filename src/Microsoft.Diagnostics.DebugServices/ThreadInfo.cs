// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Details about a native thread
    /// </summary>
    public struct ThreadInfo
    {
        public readonly int ThreadIndex;
        public readonly uint ThreadId;
        public readonly ulong ThreadTeb;

        public ThreadInfo(int index, uint id, ulong teb)
        {
            ThreadIndex = index;
            ThreadId = id;
            ThreadTeb = teb;
        }
    }
}

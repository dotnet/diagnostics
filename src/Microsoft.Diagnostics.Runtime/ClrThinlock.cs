// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// An object's thinlock.
    /// </summary>
    public class ClrThinLock
    {
        /// <summary>
        /// The owning thread of this thinlock.
        /// </summary>
        public ClrThread? Thread { get; }

        /// <summary>
        /// The recursion count of the entries for this thinlock.
        /// </summary>
        public int Recursion { get; }

        internal ClrThinLock(ClrThread? thread, int recursion)
        {
            Thread = thread;
            Recursion = recursion;
        }
    }
}

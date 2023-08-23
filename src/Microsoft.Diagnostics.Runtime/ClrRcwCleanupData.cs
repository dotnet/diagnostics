// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime
{
    public class ClrRcwCleanupData
    {
        public ulong Rcw { get; }
        public ulong Context { get; }
        public ulong Thread { get; }
        public bool IsFreeThreaded { get; }

        public ClrRcwCleanupData(ulong rcw, ulong context, ulong thread, bool isFreeThreaded)
        {
            Rcw = rcw;
            Context = context;
            Thread = thread;
            IsFreeThreaded = isFreeThreaded;
        }
    }
}
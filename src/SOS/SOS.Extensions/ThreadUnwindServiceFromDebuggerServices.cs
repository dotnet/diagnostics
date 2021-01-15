// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;

namespace SOS.Extensions
{
    internal class ThreadUnwindServiceFromDebuggerServices : IThreadUnwindService
    {
        private readonly DebuggerServices _debuggerServices;

        public ThreadUnwindServiceFromDebuggerServices(DebuggerServices debuggerServices)
        {
            _debuggerServices = debuggerServices;
        }

        public int Unwind(uint threadId, uint contextSize, byte[] context)
        {
            return _debuggerServices.VirtualUnwind(threadId, contextSize, context);
        }
    }
}

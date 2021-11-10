// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Microsoft.Diagnostics.Runtime.Utilities;
using System.Diagnostics;

namespace SOS.Extensions
{
    /// <summary>
    /// Provides the context services on native debuggers
    /// </summary>
    internal class ContextServiceFromDebuggerServices : ContextService
    {
        private readonly DebuggerServices _debuggerServices;

        internal ContextServiceFromDebuggerServices(IHost host, DebuggerServices debuggerServices)
            : base(host)
        {
            Debug.Assert(debuggerServices != null);
            _debuggerServices = debuggerServices;
        }

        public override IThread GetCurrentThread()
        {
            HResult hr = _debuggerServices.GetCurrentThreadId(out uint threadId);
            if (hr != HResult.S_OK)
            {
                Trace.TraceError("GetCurrentThreadId() FAILED {0:X8}", hr);
                return null;
            }
            return ThreadService?.GetThreadFromId(threadId);
        }

        public override void SetCurrentThread(IThread thread)
        {
            if (thread != null)
            {
                HResult hr = _debuggerServices.SetCurrentThreadId(thread.ThreadId);
                if (hr != HResult.S_OK)
                {
                    Trace.TraceError("SetCurrentThreadId() FAILED {0:X8}", hr);
                    return;
                }
            }
            base.SetCurrentThread(thread);
        }
    }
}

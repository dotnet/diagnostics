// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Microsoft.Diagnostics.Runtime.Utilities;

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

        protected override IThread GetCurrentThread()
        {
            HResult hr = _debuggerServices.GetCurrentThreadId(out uint threadId);
            if (hr != HResult.S_OK)
            {
                Trace.TraceError("GetCurrentThreadId() FAILED {0:X8}", hr);
                return null;
            }
            IThread currentThread = ThreadService?.GetThreadFromId(threadId);
            // This call fires the context change event if the thread obtain by the host debugger differs from the current thread
            base.SetCurrentThread(currentThread);
            return currentThread;
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

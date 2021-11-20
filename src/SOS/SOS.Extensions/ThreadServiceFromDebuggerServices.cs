// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace SOS.Extensions
{
    /// <summary>
    /// Provides thread and register info and values
    /// </summary>
    internal class ThreadServiceFromDebuggerServices : ThreadService
    {
        private readonly DebuggerServices _debuggerServices;

        internal ThreadServiceFromDebuggerServices(ITarget target, DebuggerServices debuggerServices)
            : base(target)
        {
            Debug.Assert(debuggerServices != null);
            _debuggerServices = debuggerServices;
        }

        protected override bool GetThreadContext(uint threadId, uint contextFlags, uint contextSize, byte[] context)
        {
            return _debuggerServices.GetThreadContext(threadId, contextFlags, contextSize, context) == HResult.S_OK;
        }

        protected override IEnumerable<IThread> GetThreadsInner()
        {
            HResult hr = _debuggerServices.GetNumberThreads(out uint number);
            if (hr == HResult.S_OK)
            {
                uint[] threadIds = new uint[number];
                uint[] threadSysIds = new uint[number];
                hr = _debuggerServices.GetThreadIdsByIndex(0, number, threadIds, threadSysIds);
                if (hr == HResult.S_OK)
                {
                    for (int i = 0; i < number; i++)
                    {
                        yield return new Thread(this, unchecked((int)threadIds[i]), threadSysIds[i]);
                    }
                }
                else
                {
                    Trace.TraceError("GetThreadIdsByIndex() FAILED {0:X8}", hr);
                }
            }
            else
            {
                Trace.TraceError("GetNumberThreads() FAILED {0:X8}", hr);
            }
        }

        protected override ulong GetThreadTeb(uint threadId)
        {
            _debuggerServices.GetThreadTeb(threadId, out ulong teb);
            return teb;
        }
    }
}

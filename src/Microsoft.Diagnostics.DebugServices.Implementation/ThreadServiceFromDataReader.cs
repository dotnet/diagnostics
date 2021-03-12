// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.DataReaders.Implementation;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// Provides thread and register info and values for the clrmd IDataReader
    /// </summary>
    public class ThreadServiceFromDataReader : ThreadService
    {
        private readonly IDataReader _dataReader;
        private readonly IThreadReader _threadReader;

        public ThreadServiceFromDataReader(ITarget target, IDataReader dataReader)
            : base(target)
        {
            _dataReader = dataReader;
            _threadReader = (IThreadReader)dataReader;

            if (dataReader is IThreadReader threadReader)
            {
                // Initialize the current thread
                IEnumerable<uint> threads = threadReader.EnumerateOSThreadIds();
                if (threads.Any()) {
                    CurrentThreadId = threads.First();
                }
            }
            else
            {
                throw new InvalidOperationException("IThreadReader not implemented");
            }
        }

        protected override bool GetThreadContext(uint threadId, uint contextFlags, uint contextSize, byte[] context)
        {
            try
            {
                return _dataReader.GetThreadContext(threadId, contextFlags, new Span<byte>(context, 0, unchecked((int)contextSize)));
            }
            catch (ClrDiagnosticsException ex)
            {
                Trace.TraceError(ex.ToString());
                return false;
            }
        }

        protected override IEnumerable<IThread> GetThreadsInner()
        {
            return _threadReader.EnumerateOSThreadIds().Select((uint id, int index) => new Thread(this, index, id)).Cast<IThread>();
        }

        protected override ulong GetThreadTeb(uint threadId)
        {
            return _threadReader.GetThreadTeb(threadId);
        }
    }
}

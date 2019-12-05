// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Microsoft.Diagnostics.NETCore.Client
{
    public class EventPipeSession : IDisposable
    {
        private IEnumerable<EventPipeProvider> _providers;
        private bool _requestRundown;
        private int _circularBufferMB;
        private long _sessionId;
        private int _processId;
        private bool disposedValue = false; // To detect redundant calls

        internal EventPipeSession(int processId, IEnumerable<EventPipeProvider> providers, bool requestRundown, int circularBufferMB)
        {
            _processId = processId;
            _providers = providers;
            _requestRundown = requestRundown;
            _circularBufferMB = circularBufferMB;
            
            var config = new EventPipeSessionConfiguration(circularBufferMB, EventPipeSerializationFormat.NetTrace, providers, requestRundown);
            var message = new IpcMessage(DiagnosticsServerCommandSet.EventPipe, (byte)EventPipeCommandId.CollectTracing2, config.SerializeV2());
            EventStream = IpcClient.SendMessage(processId, message, out var response);
            switch ((DiagnosticsServerCommandId)response.Header.CommandId)
            {
                case DiagnosticsServerCommandId.OK:
                    _sessionId = BitConverter.ToInt64(response.Payload, 0);
                    break;
                case DiagnosticsServerCommandId.Error:
                    var hr = BitConverter.ToInt32(response.Payload, 0);
                    throw new ServerErrorException($"EventPipe session start failed (HRESULT: 0x{hr:X8})");
                default:
                    throw new ServerErrorException($"EventPipe session start failed - Server responded with unknown command");
            }
        }

        public Stream EventStream { get; }

        ///<summary>
        /// Stops the given session
        ///</summary>
        public void Stop()
        {
            Debug.Assert(_sessionId > 0);

            byte[] payload = BitConverter.GetBytes(_sessionId);
            var response = IpcClient.SendMessage(_processId, new IpcMessage(DiagnosticsServerCommandSet.EventPipe, (byte)EventPipeCommandId.StopTracing, payload));

            switch ((DiagnosticsServerCommandId)response.Header.CommandId)
            {
                case DiagnosticsServerCommandId.OK:
                    return;
                case DiagnosticsServerCommandId.Error:
                    var hr = BitConverter.ToInt32(response.Payload, 0);
                    throw new ServerErrorException($"EventPipe session stop failed (HRESULT: 0x{hr:X8})");
                default:
                    throw new ServerErrorException($"EventPipe session stop failed - Server responded with unknown command");
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    EventStream?.Dispose();
                }
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
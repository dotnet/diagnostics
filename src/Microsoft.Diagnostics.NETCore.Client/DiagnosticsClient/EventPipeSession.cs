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
        private IpcEndpoint _endpoint;
        private bool _disposedValue = false; // To detect redundant calls
        private bool _stopped = false; // To detect redundant calls

        internal EventPipeSession(IpcEndpoint endpoint, IEnumerable<EventPipeProvider> providers, bool requestRundown, int circularBufferMB)
        {
            _endpoint = endpoint;
            _providers = providers;
            _requestRundown = requestRundown;
            _circularBufferMB = circularBufferMB;
            
            var config = new EventPipeSessionConfiguration(circularBufferMB, EventPipeSerializationFormat.NetTrace, providers, requestRundown);
            var message = new IpcMessage(DiagnosticsServerCommandSet.EventPipe, (byte)EventPipeCommandId.CollectTracing2, config.SerializeV2());
            EventStream = IpcClient.SendMessage(endpoint, message, out var response);
            switch ((DiagnosticsServerResponseId)response.Header.CommandId)
            {
                case DiagnosticsServerResponseId.OK:
                    _sessionId = BitConverter.ToInt64(response.Payload, 0);
                    break;
                case DiagnosticsServerResponseId.Error:
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
            
            // Do not issue another Stop command if it has already been issued for this session instance.
            if (_stopped)
            {
                return;
            }
            else
            {
                _stopped = true;
            }

            byte[] payload = BitConverter.GetBytes(_sessionId);
            IpcMessage response;
            try
            {
                response = IpcClient.SendMessage(_endpoint, new IpcMessage(DiagnosticsServerCommandSet.EventPipe, (byte)EventPipeCommandId.StopTracing, payload));
            }
            // On non-abrupt exits (i.e. the target process has already exited and pipe is gone, sending Stop command will fail).
            catch (IOException)
            {
                throw new ServerNotAvailableException("Could not send Stop command. The target process may have exited.");
            }

            switch ((DiagnosticsServerResponseId)response.Header.CommandId)
            {
                case DiagnosticsServerResponseId.OK:
                    return;
                case DiagnosticsServerResponseId.Error:
                    var hr = BitConverter.ToInt32(response.Payload, 0);
                    throw new ServerErrorException($"EventPipe session stop failed (HRESULT: 0x{hr:X8})");
                default:
                    throw new ServerErrorException($"EventPipe session stop failed - Server responded with unknown command");
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            // If session being disposed hasn't been stopped, attempt to stop it first
            if (!_stopped)
            {
                try
                {
                    Stop();
                }
                catch {} // swallow any exceptions that may be thrown from Stop.
            }

            if (!_disposedValue)
            {
                if (disposing)
                {
                    EventStream?.Dispose();
                }
                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
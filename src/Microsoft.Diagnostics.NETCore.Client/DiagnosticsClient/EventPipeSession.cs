// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
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

        internal EventPipeSession(int processId, IEnumerable<EventPipeProvider> providers, bool requestRundown, int circularBufferMB)
        {
            _processId = processId;
            _providers = providers;
            _requestRundown = requestRundown;
            _circularBufferMB = circularBufferMB;
            
            var config = new EventPipeSessionConfiguration(circularBufferMB, EventPipeSerializationFormat.NetTrace, providers, requestRundown);
            var message = new IpcMessage(DiagnosticsServerCommandSet.EventPipe, (byte)EventPipeCommandId.CollectTracing2, config.Serialize());
            EventStream = IpcClient.SendMessage(processId, message, out var response);
            
            switch ((DiagnosticsServerCommandId)response.Header.CommandId)
            {
                case DiagnosticsServerCommandId.OK:
                    _sessionId = BitConverter.ToInt64(response.Payload, 0);
                    break;
                case DiagnosticsServerCommandId.Error:
                    // bad...
                    var hr = BitConverter.ToInt32(response.Payload, 0);
                    throw new Exception($"Session start FAILED 0x{hr:X8}");
                default:
                    break;
            }
        }

        public Stream EventStream { get; }

        ///<summary>
        /// Stops the given session
        ///</summary>
        public void Stop()
        {
            // TODO
            if (_sessionId == 0)
                return; // TODO: Throw here instead?

            byte[] payload = BitConverter.GetBytes(_sessionId);

            var response = IpcClient.SendMessage(_processId, new IpcMessage(DiagnosticsServerCommandSet.EventPipe, (byte)EventPipeCommandId.StopTracing, payload));

            switch ((DiagnosticsServerCommandId)response.Header.CommandId)
            {
                case DiagnosticsServerCommandId.OK:
                    return;
                case DiagnosticsServerCommandId.Error:
                    //TODO: THROW HERE?
                    return;
                default:
                    //TODO: THROW HERE?
                    return;
            }
        }

        public void Dispose()
        {
            // TODO
            return;
        }

        protected virtual void Dispose(bool disposing)
        {
            // TODO
            return;
        }
    }
}
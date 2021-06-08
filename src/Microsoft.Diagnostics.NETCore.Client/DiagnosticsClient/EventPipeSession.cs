// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.NETCore.Client
{
    public class EventPipeSession : IDisposable
    {
        private long _sessionId;
        private IpcEndpoint _endpoint;
        private bool _disposedValue = false; // To detect redundant calls
        private bool _stopped = false; // To detect redundant calls
        private readonly IpcResponse _response;

        private EventPipeSession(IpcEndpoint endpoint, IpcResponse response, long sessionId)
        {
            _endpoint = endpoint;
            _response = response;
            _sessionId = sessionId;
        }

        public Stream EventStream => _response.Continuation;

        internal static EventPipeSession Start(IpcEndpoint endpoint, IEnumerable<EventPipeProvider> providers, bool requestRundown, int circularBufferMB)
        {
            IpcMessage requestMessage = CreateStartMessage(providers, requestRundown, circularBufferMB);
            IpcResponse response = IpcClient.SendMessage(endpoint, requestMessage);
            return new EventPipeSession(endpoint, response, ValidateStartResponse(response.Message));
        }

        internal static async Task<EventPipeSession> StartAsync(IpcEndpoint endpoint, IEnumerable<EventPipeProvider> providers, bool requestRundown, int circularBufferMB, CancellationToken cancellationToken)
        {
            IpcMessage requestMessage = CreateStartMessage(providers, requestRundown, circularBufferMB);
            IpcResponse response = await IpcClient.SendMessageAsync(endpoint, requestMessage, cancellationToken).ConfigureAwait(false);
            return new EventPipeSession(endpoint, response, ValidateStartResponse(response.Message));
        }

        ///<summary>
        /// Stops the given session
        ///</summary>
        public void Stop()
        {
            if (TryCreateStopMessage(out IpcMessage requestMessage))
            {
                IpcMessage responseMessage;
                try
                {
                    using IpcResponse response = IpcClient.SendMessage(_endpoint, requestMessage);
                    responseMessage = response.Message;
                }
                // On non-abrupt exits (i.e. the target process has already exited and pipe is gone, sending Stop command will fail).
                catch (IOException)
                {
                    throw new ServerNotAvailableException("Could not send Stop command. The target process may have exited.");
                }

                ValidateStopResponse(responseMessage);
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (TryCreateStopMessage(out IpcMessage requestMessage))
            {
                IpcMessage responseMessage;
                try
                {
                    using IpcResponse response = await IpcClient.SendMessageAsync(_endpoint, requestMessage, cancellationToken).ConfigureAwait(false);
                    responseMessage = response.Message;
                }
                // On non-abrupt exits (i.e. the target process has already exited and pipe is gone, sending Stop command will fail).
                catch (IOException)
                {
                    throw new ServerNotAvailableException("Could not send Stop command. The target process may have exited.");
                }

                ValidateStopResponse(responseMessage);
            }
        }

        private static IpcMessage CreateStartMessage(IEnumerable<EventPipeProvider> providers, bool requestRundown, int circularBufferMB)
        {
            var config = new EventPipeSessionConfiguration(circularBufferMB, EventPipeSerializationFormat.NetTrace, providers, requestRundown);
            return new IpcMessage(DiagnosticsServerCommandSet.EventPipe, (byte)EventPipeCommandId.CollectTracing2, config.SerializeV2());
        }

        private bool TryCreateStopMessage(out IpcMessage stopMessage)
        {
            Debug.Assert(_sessionId > 0);

            // Do not issue another Stop command if it has already been issued for this session instance.
            if (_stopped)
            {
                stopMessage = null;
                return false;
            }
            else
            {
                _stopped = true;
            }

            byte[] payload = BitConverter.GetBytes(_sessionId);

            stopMessage = new IpcMessage(DiagnosticsServerCommandSet.EventPipe, (byte)EventPipeCommandId.StopTracing, payload);

            return true;
        }

        private static long ValidateStartResponse(IpcMessage responseMessage)
        {
            switch ((DiagnosticsServerResponseId)responseMessage.Header.CommandId)
            {
                case DiagnosticsServerResponseId.OK:
                    return BitConverter.ToInt64(responseMessage.Payload, 0);
                case DiagnosticsServerResponseId.Error:
                    var hr = BitConverter.ToInt32(responseMessage.Payload, 0);
                    throw new ServerErrorException($"EventPipe session start failed (HRESULT: 0x{hr:X8})");
                default:
                    throw new ServerErrorException($"EventPipe session start failed - Server responded with unknown command");
            }
        }

        private static void ValidateStopResponse(IpcMessage responseMessage)
        {
            switch ((DiagnosticsServerResponseId)responseMessage.Header.CommandId)
            {
                case DiagnosticsServerResponseId.OK:
                    return;
                case DiagnosticsServerResponseId.Error:
                    var hr = BitConverter.ToInt32(responseMessage.Payload, 0);
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
                    _response.Dispose();
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
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Threading;

namespace Microsoft.Diagnostics.NETCore.Client
{
    internal class IpcClient
    {
        // The amount of time to wait for a stream to be available for consumption by the Connect method.
        // Normally expect the runtime to respond quickly but resource constrained machines may take longer.
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Sends a single DiagnosticsIpc Message to the dotnet process with PID processId.
        /// </summary>
        /// <param name="endpoint">An endpoint that provides a diagnostics connection to a runtime instance.</param>
        /// <param name="message">The DiagnosticsIpc Message to be sent</param>
        /// <returns>The response DiagnosticsIpc Message from the dotnet process</returns>
        public static IpcMessage SendMessage(IpcEndpoint endpoint, IpcMessage message)
        {
            using (var stream = endpoint.Connect(ConnectTimeout))
            {
                Write(stream, message);
                return Read(stream);
            }
        }

        /// <summary>
        /// Sends a single DiagnosticsIpc Message to the dotnet process with PID processId
        /// and returns the Stream for reuse in Optional Continuations.
        /// </summary>
        /// <param name="endpoint">An endpoint that provides a diagnostics connection to a runtime instance.</param>
        /// <param name="message">The DiagnosticsIpc Message to be sent</param>
        /// <param name="response">out var for response message</param>
        /// <returns>The response DiagnosticsIpc Message from the dotnet process</returns>
        public static Stream SendMessage(IpcEndpoint endpoint, IpcMessage message, out IpcMessage response)
        {
            var stream = endpoint.Connect(ConnectTimeout);
            Write(stream, message);
            response = Read(stream);
            return stream;
        }

        private static void Write(Stream stream, byte[] buffer)
        {
            stream.Write(buffer, 0, buffer.Length);
        }

        private static void Write(Stream stream, IpcMessage message)
        {
            Write(stream, message.Serialize());
        }

        private static IpcMessage Read(Stream stream)
        {
            return IpcMessage.Parse(stream);
        }
    }
}

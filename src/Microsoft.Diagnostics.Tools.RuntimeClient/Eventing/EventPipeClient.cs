// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace Microsoft.Diagnostics.Tools.RuntimeClient
{
    public static class EventPipeClient
    {
        private static string DiagnosticPortPattern { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"^dotnetcore-diagnostic-(\d+)$" : @"^dotnetcore-diagnostic-(\d+)-(\d+)-socket$";

        private static string IpcRootPath { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"\\.\pipe\" : Path.GetTempPath();

        private static double ConnectTimeoutMilliseconds { get; } = TimeSpan.FromSeconds(3).TotalMilliseconds;

        /// <summary>
        /// Send event pipe command.
        /// </summary>
        /// <param name="processId">runtime process id</param>
        /// <param name="buffer">serialized command</param>
        /// <returns>command result</returns>
        public static ulong SendCommand(int processId, byte[] buffer)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string pipeName = $"dotnetcore-diagnostic-{processId}";
                using (var namedPipe = new NamedPipeClientStream(
                    ".", pipeName, PipeDirection.InOut, PipeOptions.None, TokenImpersonationLevel.Impersonation))
                {
                    namedPipe.Connect((int)ConnectTimeoutMilliseconds);
                    namedPipe.Write(buffer, 0, buffer.Length);

                    return new BinaryReader(namedPipe).ReadUInt64();
                }
            }
            else
            {
                string ipcPort = Directory.GetFiles(IpcRootPath) // Try best match.
                    .Select(namedPipe => (new FileInfo(namedPipe)).Name)
                    .SingleOrDefault(input => Regex.IsMatch(input, $"^dotnetcore-diagnostic-{processId}-(\\d+)-socket$"));
                if (ipcPort == null)
                {
                    throw new PlatformNotSupportedException($"Process {processId} not running compatible .NET Core runtime");
                }
                string path = Path.Combine(Path.GetTempPath(), ipcPort);
                var remoteEP = new UnixDomainSocketEndPoint(path);

                using (var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
                {
                    socket.Connect(remoteEP);
                    socket.Send(buffer);

                    var content = new byte[sizeof(ulong)];
                    int nReceivedBytes = socket.Receive(content);
                    return (nReceivedBytes == sizeof(ulong)) ? BitConverter.ToUInt64(content, 0) : 0;
                }
            }
        }

        /// <summary>
        /// Get the files associated with the opened IPC Ports for DotNet Core applications.
        /// </summary>
        /// <returns>
        /// A collection of process identifiers associated with the list of opened files (IPC ports).
        /// These process Ids might have expired and not properly cleaned up.
        /// </returns>
        public static IEnumerable<int> ListAvailablePorts()
        {
            return Directory.GetFiles(IpcRootPath)
                .Select(namedPipe => (new FileInfo(namedPipe)).Name)
                .Where(input => Regex.IsMatch(input, DiagnosticPortPattern))
                .Select(input => int.Parse(Regex.Match(input, DiagnosticPortPattern).Groups[1].Value, NumberStyles.Integer));
        }

        /// <summary>
        /// Start trace collection.
        /// </summary>
        /// <param name="processId">Runtime process to trace</param>
        /// <param name="configuration">buffer size and provider configuration</param>
        /// <param name="sessionId">session id</param>
        /// <returns>Stream</returns>
        public static Stream CollectTracing(int processId, SessionConfiguration configuration, out ulong sessionId)
        {
            sessionId = 0;

            var header = new MessageHeader {
                RequestType = DiagnosticMessageType.CollectEventPipeTracing,
                Pid = (uint)Process.GetCurrentProcess().Id,
            };

            byte[] serializedConfiguration;
            using (var stream = new MemoryStream())
                serializedConfiguration = Serialize(header, configuration, stream);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string pipeName = $"dotnetcore-diagnostic-{processId}";
                var namedPipe = new NamedPipeClientStream(
                    ".", pipeName, PipeDirection.InOut, PipeOptions.None, TokenImpersonationLevel.Impersonation);
                namedPipe.Connect((int)ConnectTimeoutMilliseconds);

                // Request start-collection
                namedPipe.Write(serializedConfiguration, 0, serializedConfiguration.Length);

                sessionId = new BinaryReader(namedPipe).ReadUInt64();
                return namedPipe;
            }
            else
            {
                // TODO: Determine ApplicationGroupId
                string ipcPort = Directory.GetFiles(IpcRootPath) // Try best match.
                    .Select(namedPipe => (new FileInfo(namedPipe)).Name)
                    .SingleOrDefault(input => Regex.IsMatch(input, $"^dotnetcore-diagnostic-{processId}-(\\d+)-socket$"));
                if (ipcPort == null)
                {
                    throw new PlatformNotSupportedException($"Process {processId} not running compatible .NET Core runtime");
                }
                string path = Path.Combine(Path.GetTempPath(), ipcPort);
                var remoteEP = new UnixDomainSocketEndPoint(path);

                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                socket.Connect(remoteEP);

                // Request start-collection
                socket.Send(serializedConfiguration);

                var content = new byte[sizeof(ulong)];
                int nReceivedBytes = socket.Receive(content);
                sessionId = (nReceivedBytes == sizeof(ulong)) ? BitConverter.ToUInt64(content, 0) : 0;

                return new NetworkStream(socket, FileAccess.Read, true);
            }
        }

        /// <summary>
        /// Start tracing to file.
        /// </summary>
        /// <param name="processId">Runtime process to trace</param>
        /// <param name="configuration">buffer size, file path and provider configuration</param>
        /// <returns>session id</returns>
        public static ulong StartTracingToFile(int processId, SessionConfiguration configuration)
        {
            var header = new MessageHeader {
                RequestType = DiagnosticMessageType.StartEventPipeTracing,
                Pid = (uint)Process.GetCurrentProcess().Id,
            };

            byte[] serializedConfiguration;
            using (var stream = new MemoryStream())
                serializedConfiguration = Serialize(header, configuration, stream);

            return SendCommand(processId, serializedConfiguration);
        }

        /// <summary>
        /// Turn off EventPipe logging session for the specified process Id.
        /// </summary>
        /// <param name="processId">Process Id to turn off logging session.</param>
        /// <param name="sessionId">EventPipe session Id to turn off.</param>
        /// <returns>It returns sessionId if success, otherwise 0.</returns>
        public static ulong StopTracing(int processId, ulong sessionId)
        {
            if (sessionId == 0)
                return sessionId; // TODO: Throw here instead?

            var header = new MessageHeader {
                RequestType = DiagnosticMessageType.StopEventPipeTracing,
                Pid = (uint)Process.GetCurrentProcess().Id,
            };

            byte[] sessionIdInBytes;
            using (var stream = new MemoryStream())
            {
                using (var sw = new BinaryWriter(stream))
                {
                    sw.Write((uint)header.RequestType);
                    sw.Write(header.Pid);

                    sw.Write(sessionId);
                    sw.Flush();
                    sessionIdInBytes = stream.ToArray();
                }
            }

            return SendCommand(processId, sessionIdInBytes);
        }

        private static byte[] Serialize(MessageHeader header, SessionConfiguration configuration, Stream stream)
        {
            using (var bw = new BinaryWriter(stream))
            {
                bw.Write((uint)header.RequestType);
                bw.Write(header.Pid);

                bw.Write(configuration.CircularBufferSizeInMB);

                bw.WriteString(configuration.OutputPath);

                bw.Write(configuration.Providers.Count());
                foreach (var provider in configuration.Providers)
                {
                    bw.Write(provider.Keywords);
                    bw.Write((uint)provider.EventLevel);

                    bw.WriteString(provider.Name);
                    bw.WriteString(provider.FilterData);
                }

                bw.Flush();
                stream.Position = 0;

                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                return bytes;
            }
        }
    }
}

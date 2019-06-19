using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace Microsoft.Diagnostics.Tools.RuntimeClient.DiagnosticsIpc
{
    public class IpcClient
    {
        private static string DiagnosticPortPattern { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"^dotnet-diagnostic-(\d+)$" : @"^dotnet-diagnostic-(\d+)-(\d+)-socket$";

        private static string IpcRootPath { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"\\.\pipe\" : Path.GetTempPath();

        private static double ConnectTimeoutMilliseconds { get; } = TimeSpan.FromSeconds(3).TotalMilliseconds;

        /// <summary>
        /// Get the OS Transport to be used for communicating with a dotnet process.
        /// </summary>
        /// <param name="processId">The PID of the dotnet process to get the transport for</param>
        /// <returns>A System.IO.Stream wrapper around the transport</returns>
        private static Stream GetTransport(int processId)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string pipeName = $"dotnet-diagnostic-{processId}";
                var namedPipe = new NamedPipeClientStream(
                    ".", pipeName, PipeDirection.InOut, PipeOptions.None, TokenImpersonationLevel.Impersonation);
                namedPipe.Connect((int)ConnectTimeoutMilliseconds);
                return namedPipe;
            }
            else
            {
                string ipcPort = Directory.GetFiles(IpcRootPath) // Try best match.
                    .Select(namedPipe => (new FileInfo(namedPipe)).Name)
                    .SingleOrDefault(input => Regex.IsMatch(input, $"^dotnet-diagnostic-{processId}-(\\d+)-socket$"));
                if (ipcPort == null)
                {
                    throw new PlatformNotSupportedException($"Process {processId} not running compatible .NET Core runtime");
                }
                string path = Path.Combine(Path.GetTempPath(), ipcPort);
                var remoteEP = new UnixDomainSocketEndPoint(path);

                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                socket.Connect(remoteEP);
                return new NetworkStream(socket);
            }
        }

        /// <summary>
        /// Sends a single DiagnosticIpc Message to the dotnet process with PID processId.
        /// </summary>
        /// <param name="processId">The PID of the dotnet process</param>
        /// <param name="message">The DiagnosticsIpc Message to be sent</param>
        /// <returns>The response DiagnosticsIpc Message from the dotnet process</returns>
        public static IpcMessage SendMessage(int processId, IpcMessage message)
        {
            using (var stream = GetTransport(processId))
            {
                Write(stream, message);
                return Read(stream);
            }
        }

        /// <summary>
        /// Sends a single DiagnosticIpc Message to the dotnet process with PID processId
        /// and returns the Stream for reuse in Optional Continuations.
        /// </summary>
        /// <param name="processId">The PID of the dotnet process</param>
        /// <param name="message">The DiagnosticsIpc Message to be sent</param>
        /// <param name="response">out var for response message</param>
        /// <returns>The response DiagnosticsIpc Message from the dotnet process</returns>
        public static Stream SendMessage(int processId, IpcMessage message, out IpcMessage response)
        {
            var stream = GetTransport(processId);
            Write(stream, message);
            response = Read(stream);
            return stream;
        }

        private static void Write(Stream stream, byte[] buffer)
        {
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(buffer);
            }
        }

        private static void Write(Stream stream, IpcMessage message)
        {
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(message.Serialize());
            }
        }


        private static IpcMessage Read(Stream stream)
        {
            return IpcMessage.Parse(stream);
        }
    }
}

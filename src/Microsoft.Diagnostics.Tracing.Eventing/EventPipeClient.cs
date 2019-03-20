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
using System.Text;
using System.Text.RegularExpressions;

namespace Microsoft.Diagnostics.Tracing.Eventing
{
    public static class EventPipeClient
    {
        public static IEnumerable<int> ListAvailablePorts()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                const string DiagnosticPortPattern = @"^dotnetcore-diagnostic-(\d+)$";
                return Directory.GetFiles(@"\\.\pipe\")
                    .Select(namedPipe => (new FileInfo(namedPipe)).Name)
                    .Where(input => Regex.IsMatch(input, DiagnosticPortPattern))
                    .Select(input => int.Parse(Regex.Match(input, DiagnosticPortPattern).Groups[1].Value, NumberStyles.Integer));
            }
            else
            {
                const string DiagnosticPortPattern = @"^dotnetcore-diagnostic-(\d+)-(\d+)-socket$";
                return Directory.GetFiles(Path.GetTempPath())
                    .Select(namedPipe => (new FileInfo(namedPipe)).Name)
                    .Where(input => Regex.IsMatch(input, DiagnosticPortPattern))
                    .Select(input => int.Parse(Regex.Match(input, DiagnosticPortPattern).Groups[1].Value, NumberStyles.Integer));
            }
        }

        public static ulong EnableTracingToFile(int processId, ProviderConfiguration providerConfiguration)
        {
            var header = new MessageHeader {
                RequestType = DiagnosticMessageType.Enable,
                Pid = (uint)Process.GetCurrentProcess().Id,
            };

            byte[] serializedConfiguration;
            using (var stream = new MemoryStream())
            {
                serializedConfiguration = Serialize(header, providerConfiguration, stream);
                Console.WriteLine($"Serialized data is {serializedConfiguration.Length} bytes vs {GetByteCount(providerConfiguration)}.");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var pipeName = $"dotnetcore-diagnostic-{processId}";
                using (var namedPipe = new NamedPipeClientStream(
                    ".", pipeName, PipeDirection.InOut, PipeOptions.None, TokenImpersonationLevel.Impersonation))
                {
                    namedPipe.Connect((int)TimeSpan.FromSeconds(20).TotalMilliseconds);

                    var sw = new BinaryWriter(namedPipe);
                    sw.Write(serializedConfiguration);

                    var br = new BinaryReader(namedPipe);
                    return br.ReadUInt64();
                }
            }
            else
            {
                throw new PlatformNotSupportedException("TODO: Get the ApplicationGroupId to form the string: 'dotnetcore-diagnostic-{processId}-{ApplicationGroupId}-socket'");
            }
        }

        public static ulong DisableTracingToFile(int processId, ulong sessionId)
        {
            var header = new MessageHeader {
                RequestType = DiagnosticMessageType.Disable,
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
                    stream.Position = 0;
                    sessionIdInBytes = new byte[stream.Length];
                    stream.Read(sessionIdInBytes, 0, sessionIdInBytes.Length);
                }
                Console.WriteLine($"Serialized data is {sessionIdInBytes.Length} bytes vs {sizeof(ulong)}.");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var pipeName = $"dotnetcore-diagnostic-{processId}";
                using (var namedPipe = new NamedPipeClientStream(
                    ".", pipeName, PipeDirection.InOut, PipeOptions.None, TokenImpersonationLevel.Impersonation))
                {
                    namedPipe.Connect((int)TimeSpan.FromSeconds(20).TotalMilliseconds);

                    var sw = new BinaryWriter(namedPipe);
                    sw.Write(sessionIdInBytes);
                    return sessionId;
                }
            }
            else
            {
                using (var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
                {
                    var remoteEP = new UnixDomainSocketEndPoint("/tmp/dotnetcore-diagnostic-1234.socket");
                    socket.Bind(remoteEP);
                    socket.Connect(remoteEP);

                    socket.Send(sessionIdInBytes);
                    return sessionId;
                }
            }
        }

        private static int GetSizeInBytes(string str)
        {
            var strLength = str == null ? 0 : Encoding.Unicode.GetByteCount(str + '\0');
            return Marshal.SizeOf(typeof(int)) + strLength;
        }

        private static int GetByteCount(ProviderConfiguration providerConfiguration)
        {
            int size = 0;

            size += Marshal.SizeOf(providerConfiguration.CircularBufferSizeInMB.GetType());
            size += Marshal.SizeOf(providerConfiguration.MultiFileTraceLengthInSeconds.GetType());

            size += GetSizeInBytes(providerConfiguration.OutputPath);

            size += Marshal.SizeOf(typeof(int));
            foreach (var provider in providerConfiguration.Providers)
            {
                size += Marshal.SizeOf(provider.Keywords.GetType());
                size += Marshal.SizeOf(typeof(uint)); // provider.EventLevel.GetType()
                size += GetSizeInBytes(provider.Name);
                size += GetSizeInBytes(provider.FilterData);
            }

            return size;
        }

        private static void WriteString(string value, BinaryWriter sw)
        {
            sw.Write(value != null ? (value.Length + 1) : 0);
            if (value != null)
                sw.Write(Encoding.Unicode.GetBytes(value + '\0'));
        }

        private static byte[] Serialize(MessageHeader header, ProviderConfiguration providerConfiguration, Stream stream)
        {
            using (var sw = new BinaryWriter(stream))
            {
                sw.Write((uint)header.RequestType);
                sw.Write(header.Pid);

                sw.Write(providerConfiguration.CircularBufferSizeInMB);
                sw.Write(providerConfiguration.MultiFileTraceLengthInSeconds);

                WriteString(providerConfiguration.OutputPath, sw);

                sw.Write(providerConfiguration.Providers.Count());
                foreach (var provider in providerConfiguration.Providers)
                {
                    sw.Write(provider.Keywords);
                    sw.Write((uint)provider.EventLevel);

                    WriteString(provider.Name, sw);
                    WriteString(provider.FilterData, sw);
                }

                sw.Flush();
                stream.Position = 0;

                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                return bytes;
            }

        }
    }
}

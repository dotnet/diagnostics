// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Microsoft.Diagnostics.NETCore.Client
{
    internal class IpcTransport
    {
        private string _ipcTransportPath = null;
        private int? _pid = null;

        private static double ConnectTimeoutMilliseconds { get; } = TimeSpan.FromSeconds(3).TotalMilliseconds;
        public static string IpcRootPath { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"\\.\pipe\" : Path.GetTempPath();
        public static string DiagnosticsPortPattern { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"^dotnet-diagnostic-(\d+)$" : @"^dotnet-diagnostic-(\d+)-(\d+)-socket$";

        /// <summary>
        /// Creates a reference to a .NET process's IPC Transport
        /// by getting a reference to the Named Pipe or Socket
        /// specified in ipcTransportPath
        /// </summary>
        /// <param name="ipcTransportPath">The fully qualified path, including filename, of the IPC Transport</param>
        /// <returns>A reference to the IPC Transport</returns>
        public IpcTransport(string ipcTransportPath)
        {
            _ipcTransportPath = ipcTransportPath;
        }

        /// <summary>
        /// Creates a reference to a .NET process's IPC Transport
        /// using the default rules for a given pid
        /// </summary>
        /// <param name="pid">The pid of the target process</param>
        /// <returns>A reference to the IPC Transport</returns>
        public IpcTransport(int pid)
        {
            _pid = pid;
        }

        /// <summary>
        /// Connects to the underlying IPC Transport and opens a read/write-able Stream
        /// </summary>
        /// <returns>A Stream for writing and reading data to and from the target .NET process</returns>
        public Stream Connect()
        {
            if (!string.IsNullOrEmpty(_ipcTransportPath))
            {
                return ConnectViaPath();
            }
            else if (_pid.HasValue)
            {
                return ConnectViaPid();
            }
            else
            {
                throw new ApplicationException("Cannot connect to Diagnostics IPC Transport without a PID or specific path");
            }
        }

        private Stream ConnectViaPath()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var normalizedPath = _ipcTransportPath.StartsWith(@"\\.\pipe\") ? _ipcTransportPath.Substring(9) : _ipcTransportPath;
                var namedPipe = new NamedPipeClientStream(
                    ".", normalizedPath, PipeDirection.InOut, PipeOptions.None, TokenImpersonationLevel.Impersonation);
                namedPipe.Connect((int)ConnectTimeoutMilliseconds);
                return namedPipe;
            }
            else
            {
                var remoteEP = CreateUnixDomainSocketEndPoint(_ipcTransportPath);

                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                socket.Connect(remoteEP);
                return new NetworkStream(socket);
            }
        }

        private Stream ConnectViaPid()
        {
            Debug.Assert(_pid.HasValue);
            try
            {
                var process = Process.GetProcessById(_pid.Value);
            }
            catch (System.ArgumentException)
            {
                throw new ServerNotAvailableException($"Process {_pid.Value} is not running.");
            }
            catch (System.InvalidOperationException)
            {
                throw new ServerNotAvailableException($"Process {_pid.Value} seems to be elevated.");
            }
 
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string pipeName = $"dotnet-diagnostic-{_pid.Value}";
                var namedPipe = new NamedPipeClientStream(
                    ".", pipeName, PipeDirection.InOut, PipeOptions.None, TokenImpersonationLevel.Impersonation);
                namedPipe.Connect((int)ConnectTimeoutMilliseconds);
                return namedPipe;
            }
            else
            {
                string ipcPort;
                try
                {
                    ipcPort = Directory.GetFiles(IpcRootPath, $"dotnet-diagnostic-{_pid.Value}-*-socket") // Try best match.
                                .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                                .FirstOrDefault();
                    if (ipcPort == null)
                    {
                        throw new ServerNotAvailableException($"Process {_pid.Value} not running compatible .NET Core runtime.");
                    }
                }
                catch (InvalidOperationException)
                {
                    throw new ServerNotAvailableException($"Process {_pid.Value} not running compatible .NET Core runtime.");
                }
                string path = Path.Combine(IpcRootPath, ipcPort);
                var remoteEP = CreateUnixDomainSocketEndPoint(path);

                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                socket.Connect(remoteEP);
                return new NetworkStream(socket);
            }
        }

        private static EndPoint CreateUnixDomainSocketEndPoint(string path)
        {
#if NETCOREAPP
            return new UnixDomainSocketEndPoint(path);
#elif NETSTANDARD2_0
            // UnixDomainSocketEndPoint is not part of .NET Standard 2.0
            var type = typeof(Socket).Assembly.GetType("System.Net.Sockets.UnixDomainSocketEndPoint")
                       ?? Type.GetType("System.Net.Sockets.UnixDomainSocketEndPoint, System.Core");
            if (type == null)
            {
                throw new PlatformNotSupportedException("Current process is not running a compatible .NET Core runtime.");
            }
            var ctor = type.GetConstructor(new[] { typeof(string) });
            return (EndPoint)ctor.Invoke(new object[] { path });
#endif
        }
    }
}
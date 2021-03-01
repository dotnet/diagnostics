// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client.DiagnosticsIpc;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.NETCore.Client
{
    internal sealed class IpcUnixDomainSocketTransport : IpcSocketTransport
    {
        private bool _ownsSocketFile;
        private string _path;

        static public IpcUnixDomainSocketTransport Create(string address)
        {
            return new IpcUnixDomainSocketTransport(address);
        }

        public IpcUnixDomainSocketTransport(string address) :
            base(CreateUnixDomainSocketEndPoint(address), SocketType.Stream, ProtocolType.Unspecified)
        {
            _path = address;
        }

        public override void Bind()
        {
            base.Bind ();
            _ownsSocketFile = true;
        }

        public override void Connect(TimeSpan timeout)
        {
            base.Connect (timeout);
            _ownsSocketFile = false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_ownsSocketFile && !string.IsNullOrEmpty(_path) && File.Exists(_path))
                {
                    File.Delete(_path);
                }
            }
            base.Dispose(disposing);
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
                throw new PlatformNotSupportedException("Current process is not running a compatible .NET runtime.");
            }
            var ctor = type.GetConstructor(new[] { typeof(string) });
            return (EndPoint)ctor.Invoke(new object[] { path });
#endif
        }
    }
}

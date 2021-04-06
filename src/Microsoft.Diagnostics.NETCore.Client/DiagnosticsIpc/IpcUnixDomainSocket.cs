// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace Microsoft.Diagnostics.NETCore.Client
{
    internal sealed class IpcUnixDomainSocketEndPoint
    {
        public string Path { get; }
        public EndPoint EndPoint { get; }

        public IpcUnixDomainSocketEndPoint(string endPoint)
        {
            Path = endPoint;
            EndPoint = CreateEndPoint(endPoint);
        }

        public static implicit operator EndPoint(IpcUnixDomainSocketEndPoint endPoint) => endPoint.EndPoint;

        private static EndPoint CreateEndPoint(string endPoint)
        {
#if NETCOREAPP
            return new UnixDomainSocketEndPoint(endPoint);
#elif NETSTANDARD2_0
            // UnixDomainSocketEndPoint is not part of .NET Standard 2.0
            var type = typeof(Socket).Assembly.GetType("System.Net.Sockets.UnixDomainSocketEndPoint")
                        ?? Type.GetType("System.Net.Sockets.UnixDomainSocketEndPoint, System.Core");
            if (type == null)
            {
                throw new PlatformNotSupportedException("Current process is not running a compatible .NET runtime.");
            }
            var ctor = type.GetConstructor(new[] { typeof(string) });
            return (EndPoint)ctor.Invoke(new object[] { endPoint });
#endif
        }
    }

    internal sealed class IpcUnixDomainSocket : IpcSocket
    {
        private bool _ownsSocketFile;
        private string _path;

        internal IpcUnixDomainSocket()
            : base(SocketType.Stream, ProtocolType.Unspecified)
        {
        }

        public void Bind(IpcUnixDomainSocketEndPoint localEP)
        {
            base.Bind(localEP);
            _path = localEP.Path;
            _ownsSocketFile = true;
        }

        public override void Connect(EndPoint localEP, TimeSpan timeout)
        {
            base.Connect(localEP, timeout);
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
    }
}

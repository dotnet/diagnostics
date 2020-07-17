// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace Microsoft.Diagnostics.NETCore.Client
{
    internal sealed class UnixDomainSocket : Socket
    {
        private bool _ownsSocketFile;
        private string _path;

        public UnixDomainSocket() :
            base(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
        {
        }

        public void Bind(string path)
        {
            Bind(CreateUnixDomainSocketEndPoint(path));

            _ownsSocketFile = true;
            _path = path;
        }

        public void Connect(string path)
        {
            Connect(CreateUnixDomainSocketEndPoint(path));

            _ownsSocketFile = false;
            _path = path;
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
            var type = typeof(Socket).Assembly.GetType("System.Net.Sockets.UnixDomainSocketEndPoint");
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

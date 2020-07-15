// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Net.Sockets;

namespace Microsoft.Diagnostics.NETCore.Client
{
    internal sealed class UnixDomainSocketWithCleanup : Socket
    {
        private string _path;

        public UnixDomainSocketWithCleanup() :
            base(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
        {
        }

        public void Bind(string path)
        {
            Bind(PidIpcEndpoint.CreateUnixDomainSocketEndPoint(path));

            _path = path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!string.IsNullOrEmpty(_path) && File.Exists(_path))
                {
                    File.Delete(_path);
                }
            }
            base.Dispose(disposing);
        }
    }
}

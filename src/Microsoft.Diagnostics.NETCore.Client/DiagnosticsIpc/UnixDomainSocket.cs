// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

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

        public async Task<Socket> AcceptAsync(CancellationToken token)
        {
            using (token.Register(() => Close(0)))
            {
                try
                {
                    return await Task.Factory.FromAsync(BeginAccept, EndAccept, this).ConfigureAwait(false);
                }
                // When the socket is closed, the FromAsync logic will try to call EndAccept on the socket,
                // but that will throw an ObjectDisposedException. Only catch the exception if due to cancellation.
                catch (ObjectDisposedException) when (token.IsCancellationRequested)
                {
                    // First check if the cancellation token caused the closing of the socket,
                    // then rethrow the exception if it did not.
                    token.ThrowIfCancellationRequested();

                    Debug.Fail("Token should have thrown cancellation exception.");
                    return null;
                }
            }
        }

        public void Bind(string path)
        {
            Bind(CreateUnixDomainSocketEndPoint(path));

            _ownsSocketFile = true;
            _path = path;
        }

        public void Connect(string path, TimeSpan timeout)
        {
            IAsyncResult result = BeginConnect(CreateUnixDomainSocketEndPoint(path), null, null);

            if (result.AsyncWaitHandle.WaitOne(timeout))
            {
                EndConnect(result);

                _ownsSocketFile = false;
                _path = path;
            }
            else
            {
                Close(0);
                throw new TimeoutException();
            }
        }

        public async Task ConnectAsync(string path, CancellationToken token)
        {
            using (token.Register(() => Close(0)))
            {
                try
                {
                    Func<AsyncCallback, object, IAsyncResult> beginConnect = (callback, state) =>
                    {
                        return BeginConnect(CreateUnixDomainSocketEndPoint(path), callback, state);
                    };
                    await Task.Factory.FromAsync(beginConnect, EndConnect, this).ConfigureAwait(false);
                }
                // When the socket is closed, the FromAsync logic will try to call EndAccept on the socket,
                // but that will throw an ObjectDisposedException. Only catch the exception if due to cancellation.
                catch (ObjectDisposedException) when (token.IsCancellationRequested)
                {
                    // First check if the cancellation token caused the closing of the socket,
                    // then rethrow the exception if it did not.
                    token.ThrowIfCancellationRequested();
                }
            }
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

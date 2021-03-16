﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.NETCore.Client
{
    internal class IpcSocketTransport : Socket
    {
        EndPoint _address;

        public IpcSocketTransport(EndPoint address, SocketType type, ProtocolType protocol)
            : base(address.AddressFamily, type, protocol)
        {
            _address = address;
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

        public virtual void Bind()
        {
            Bind(_address);
        }

        public virtual void Connect(TimeSpan timeout)
        {
            IAsyncResult result = BeginConnect(_address, null, null);

            if (result.AsyncWaitHandle.WaitOne(timeout))
            {
                EndConnect(result);
            }
            else
            {
                Close(0);
                throw new TimeoutException();
            }
        }

        public async Task ConnectAsync(CancellationToken token)
        {
            using (token.Register(() => Close(0)))
            {
                try
                {
                    Func<AsyncCallback, object, IAsyncResult> beginConnect = (callback, state) =>
                    {
                        return BeginConnect(_address, callback, state);
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

    }
}

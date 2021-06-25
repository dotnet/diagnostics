// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;

namespace Microsoft.Diagnostics.NETCore.Client
{
    internal struct IpcResponse : IDisposable
    {
        public readonly IpcMessage Message;

        public readonly Stream Continuation;

        public IpcResponse(IpcMessage message, Stream continuation)
        {
            Message = message;
            Continuation = continuation;
        }

        public void Dispose()
        {
            Continuation?.Dispose();
        }
    }
}

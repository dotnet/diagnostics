// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.Egress
{
    internal abstract class EgressEndpoint<TOptions>
    {
        public virtual Task<EgressResult> EgressAsync(
            Func<CancellationToken, Task<Stream>> action,
            string name,
            TOptions streamOptions,
            CancellationToken token)
        {
            return EgressAsync(
                async (outputStream, token) =>
                {
                    using var inputStream = await action(token);

                    await inputStream.CopyToAsync(outputStream, 0x1000, token);
                },
                name,
                streamOptions,
                token);
        }

        public abstract Task<EgressResult> EgressAsync(
            Func<Stream, CancellationToken, Task> action,
            string name,
            TOptions streamOptions,
            CancellationToken token);
    }
}

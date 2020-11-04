// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.Egress
{
    internal abstract class EgressProvider<TProviderOptions, TStreamOptions>
        where TProviderOptions : EgressProviderOptions
    {
        protected EgressProvider(TProviderOptions options)
        {
            Options = options;
        }

        public virtual Task<string> EgressAsync(
            Func<CancellationToken, Task<Stream>> action,
            string name,
            TStreamOptions streamOptions,
            CancellationToken token)
        {
            return EgressAsync(
                async (targetStream, token) =>
                {
                    using var sourceStream = await action(token);

                    await sourceStream.CopyToAsync(
                        targetStream,
                        Options.CopyBufferSize.GetValueOrDefault(0x100000),
                        token);
                },
                name,
                streamOptions,
                token);
        }

        public abstract Task<string> EgressAsync(
            Func<Stream, CancellationToken, Task> action,
            string name,
            TStreamOptions streamOptions,
            CancellationToken token);

        protected TProviderOptions Options { get; }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.Egress
{
    internal abstract class EgressProvider<TProviderOptions, TStreamOptions>
        where TProviderOptions : EgressProviderOptions
    {
        protected EgressProvider(TProviderOptions options, ILogger logger = null)
        {
            Logger = logger;
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

                    int copyBufferSize = Options.CopyBufferSize.GetValueOrDefault(0x100000);

                    Logger?.LogDebug("Copying action stream to egress stream with buffer size {0}", copyBufferSize);

                    await sourceStream.CopyToAsync(
                        targetStream,
                        copyBufferSize,
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

        protected void ValidateOptions()
        {
            ValidationContext context = new ValidationContext(Options);
            Validator.ValidateObject(Options, context, validateAllProperties: true);
        }

        protected ILogger Logger { get; }

        protected TProviderOptions Options { get; }
    }
}

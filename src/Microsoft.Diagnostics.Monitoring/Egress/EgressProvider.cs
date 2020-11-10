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
    /// <summary>
    /// Base class for all egress implementations.
    /// </summary>
    /// <typeparam name="TProviderOptions">Type of provider options class.</typeparam>
    /// <typeparam name="TStreamOptions">Type of stream options class.</typeparam>
    /// <remarks>
    /// The <typeparamref name="TProviderOptions"/> type is typically used for providing information
    /// about to where a stream is egressed (e.g. directory path, blob storage account, etc).
    /// The <typeparamref name="TStreamOptions"/> type is typically used for providing information
    /// about the storage of the stream itself (e.g. file system permissions, file metadata, etc).
    /// Egress providers should throw <see cref="EgressException"/> when operational error occurs
    /// (e.g. unable to write out stream data). Nearly all other exceptions are treats as programming
    /// errors with the exception of <see cref="OperationCanceledException"/> and <see cref="ValidationException"/>.</remarks>
    internal abstract class EgressProvider<TProviderOptions, TStreamOptions>
        where TProviderOptions : EgressProviderOptions
    {
        protected EgressProvider(TProviderOptions options, ILogger logger = null)
        {
            Logger = logger;
            Options = options;
        }

        /// <summary>
        /// Egress a stream via a callback by returning the stream from the callback.
        /// </summary>
        /// <param name="action">Callback that is invoked in order to get the stream to be egressed.</param>
        /// <param name="name">The name of the stream, typically used as a file name.</param>
        /// <param name="streamOptions">Additional information to apply to the storage of the stream data.</param>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        /// <returns>A task that completes with a value of the identifier of the egress result. Typically,
        /// this is a path to access the stream without any information indicating whether any particular
        /// user has access to it (e.g. no file system permissions or SAS tokens).</returns>
        public virtual Task<string> EgressAsync(
            Func<CancellationToken, Task<Stream>> action,
            string name,
            TStreamOptions streamOptions,
            CancellationToken token)
        {
            Func<Stream, CancellationToken, Task> wrappingAction = async (targetStream, token) =>
            {
                using var sourceStream = await action(token);

                int copyBufferSize = Options.CopyBufferSize.GetValueOrDefault(0x100000);

                Logger?.LogDebug("Copying action stream to egress stream with buffer size {0}", copyBufferSize);

                await sourceStream.CopyToAsync(
                    targetStream,
                    copyBufferSize,
                    token);
            };

            return EgressAsync(
                wrappingAction,
                name,
                streamOptions,
                token);
        }

        /// <summary>
        /// Egress a stream via a callback by writing to the provided stream.
        /// </summary>
        /// <param name="action">Callback that is invoked in order to write data to the provided stream.</param>
        /// <param name="name">The name of the stream, typically used as a file name.</param>
        /// <param name="streamOptions">Additional information to apply to the storage of the stream data.</param>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        /// <returns>A task that completes with a value of the identifier of the egress result. Typically,
        /// this is a path to access the stream without any information indicating whether any particular
        /// user has access to it (e.g. no file system permissions or SAS tokens).</returns>
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

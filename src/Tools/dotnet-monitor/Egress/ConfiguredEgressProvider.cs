// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Monitoring;
using Microsoft.Diagnostics.Monitoring.RestServer;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Tools.Monitor
{
    /// <summary>
    /// Base class for configured egress providers. A configured egress provider is an egress provider instance
    /// that does not require any additional configuration beyond information about the stream that will be egressed.
    /// </summary>
    internal abstract class ConfiguredEgressProvider
    {
        /// <summary>
        /// Egress a stream via a callback by returning the stream from the callback.
        /// </summary>
        /// <param name="action">Callback that is invoked in order to get the stream to be egressed.</param>
        /// <param name="fileName">The name of the stream data, typically used as the file name.</param>
        /// <param name="contentType">The type of content contained by the stream.</param>
        /// <param name="source">The source of the egress artifact; for for interrogation purposed to fill out additional stream option data.</param>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        /// <returns>A task that completes with an <see cref="EgressResult"/> describing the completion of the egress operation.</returns>
        public abstract Task<EgressResult> EgressAsync(
            Func<CancellationToken, Task<Stream>> action,
            string fileName,
            string contentType,
            IEndpointInfo source,
            CancellationToken token);

        /// <summary>
        /// Egress a stream via a callback by writing to the provided stream.
        /// </summary>
        /// <param name="action">Callback that is invoked in order to write data to the provided stream.</param>
        /// <param name="fileName">The name of the stream data, typically used as the file name.</param>
        /// <param name="contentType">The type of content contained by the stream.</param>
        /// <param name="source">The source of the egress artifact; for for interrogation purposed to fill out additional stream option data.</param>
        /// <param name="token">The token to monitor for cancellation requests.</param>
        /// <returns>A task that completes with an <see cref="EgressResult"/> describing the completion of the egress operation.</returns>
        public abstract Task<EgressResult> EgressAsync(
            Func<Stream, CancellationToken, Task> action,
            string fileName,
            string contentType,
            IEndpointInfo source,
            CancellationToken token);
    }
}

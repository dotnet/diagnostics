// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.Diagnostics.Tools.Monitor.Egress
{
    /// <summary>
    /// Egress provider options common to all egress providers.
    /// </summary>
    internal abstract class EgressProviderOptions
    {
        /// <summary>
        /// Buffer size used when copying data from an egress callback returning a stream
        /// to the egress callback that is provided a stream to which data is written.
        /// </summary>
        public int? CopyBufferSize { get; set; }
    }
}

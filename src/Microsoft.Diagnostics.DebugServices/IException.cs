// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Describes a managed exception
    /// </summary>
    public interface IException
    {
        /// <summary>
        /// Exception object address
        /// </summary>
        ulong Address { get; }

        /// <summary>
        /// The exception type name
        /// </summary>
        string Type { get; }

        /// <summary>
        /// The exception message
        /// </summary>
        string Message { get; }

        /// <summary>
        /// Exception.HResult
        /// </summary>
        uint HResult { get; }

        /// <summary>
        /// Stack trace of exception or null
        /// </summary>
        IStack Stack { get; }

        /// <summary>
        /// The inner exception or exceptions in the AggregateException case
        /// </summary>
        IEnumerable<IException> InnerExceptions { get; }
    }
}

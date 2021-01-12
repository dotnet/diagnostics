// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.Diagnostics.Tools.Monitor
{
    /// <summary>
    /// Metadata keys that correspond to <see cref="System.Diagnostics.Activity"/> properties.
    /// </summary>
    internal static class ActivityMetadataNames
    {
        /// <summary>
        /// Represents the ID of the parent activity.
        /// </summary>
        /// <remarks>
        /// This name is the same as logged by the ActivityLogScope.
        /// </remarks>
        public const string ParentId = nameof(ParentId);

        /// <summary>
        /// Represents the ID of the current activity.
        /// </summary>
        /// <remarks>
        /// This name is the same as logged by the ActivityLogScope.
        /// </remarks>
        public const string SpanId = nameof(SpanId);

        /// <summary>
        /// Represents the trace ID of the activity.
        /// </summary>
        /// <remarks>
        /// This name is the same as logged by the ActivityLogScope.
        /// </remarks>
        public const string TraceId = nameof(TraceId);
    }
}

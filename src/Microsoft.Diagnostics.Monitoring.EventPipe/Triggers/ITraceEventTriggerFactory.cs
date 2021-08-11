// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.Diagnostics.Monitoring.EventPipe.Triggers
{
    /// <summary>
    /// Interface for creating a new instance of the associated
    /// trigger from the specified settings.
    /// </summary>
    internal interface ITraceEventTriggerFactory<TSettings>
    {
        /// <summary>
        /// Creates a new instance of the associated trigger from the <paramref name="settings"/>.
        /// </summary>
        ITraceEventTrigger Create(TSettings settings);
    }
}

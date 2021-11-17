// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// An event interface for services.
    /// </summary>
    public interface IServiceEvent
    {
        /// <summary>
        /// Register for the event callback. Puts the new callback at the end of the list.
        /// </summary>
        /// <param name="callback">callback delegate</param>
        /// <returns>An opaque IDisposable that will unregister the callback when disposed</returns>
        IDisposable Register(Action callback);

        /// <summary>
        /// Register for the event callback. Puts the new callback at the end of the list. Automatically 
        /// removed from the event list when fired.
        /// </summary>
        /// <param name="callback">callback delegate</param>
        /// <returns>An opaque IDisposable that will unregister the callback when disposed</returns>
        IDisposable RegisterOneShot(Action callback);

        /// <summary>
        /// Fires the event
        /// </summary>
        void Fire();
    }
}

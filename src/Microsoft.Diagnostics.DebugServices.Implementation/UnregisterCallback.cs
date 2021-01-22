// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// Helper class that implements unregister callback
    /// </summary>
    public class UnregisterCallback : IDisposable
    {
        Action m_action;

        public UnregisterCallback(Action action)
        {
            m_action = action;
        }

        public void Dispose()
        {
            var action = m_action;
            if (action != null) {
                m_action = null;
                action();
            }
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// The service event implementation
    /// </summary>
    public class ServiceEvent : IServiceEvent
    {
        private class EventNode : LinkedListNode, IDisposable
        {
            private readonly Action _callback;

            internal EventNode(Action callback)
            {
                _callback = callback;
            }

            internal void Fire()
            {
                _callback();
            }

            void IDisposable.Dispose()
            {
                Remove();
            }
        }

        private readonly LinkedListNode _events = new LinkedListNode();

        public ServiceEvent()
        {
        }

        public IDisposable Register(Action callback)
        {
            // Insert at the end of the list
            var node = new EventNode(callback);
            _events.InsertBefore(node);
            return node;
        }

        public void Fire()
        {
            foreach (EventNode node in _events.GetValues<EventNode>())
            {
                node.Fire();
            }
        }
    }
}

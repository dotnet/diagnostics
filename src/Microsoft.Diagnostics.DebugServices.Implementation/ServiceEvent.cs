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
        private class EventNode : LinkedListNode
        {
            private readonly WeakReference<Action> _callback;

            internal EventNode(Action callback)
            {
                _callback = new WeakReference<Action>(callback);
            }

            internal void Fire()
            {
                if (_callback.TryGetTarget(out Action target))
                {
                    target();
                }
                else
                {
                    Remove();
                }
            }
        }

        private class Unregister : IDisposable
        {
            private readonly EventNode _eventNode;
            private readonly Action _callback;

            internal Unregister(EventNode eventNode, Action callback)
            {
                _eventNode = eventNode;
                _callback = callback;
            }

            void IDisposable.Dispose()
            {
                _eventNode.Remove();
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
            return new Unregister(node, callback);
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

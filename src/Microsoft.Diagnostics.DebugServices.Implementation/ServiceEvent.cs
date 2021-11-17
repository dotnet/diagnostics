// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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

            internal EventNode(bool oneshot, Action callback)
            {
                if (oneshot)
                {
                    _callback = () => {
                        callback();
                        Remove();
                    };
                }
                else
                {
                    _callback = callback;
                }
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

        public IDisposable Register(Action callback) => Register(oneshot: false, callback);

        public IDisposable RegisterOneShot(Action callback) => Register(oneshot: true, callback);

        private IDisposable Register(bool oneshot, Action callback)
        {
            // Insert at the end of the list
            var node = new EventNode(oneshot, callback);
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

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
        private sealed class EventNode : LinkedListNode, IDisposable
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

        private readonly LinkedListNode _events = new();

        public ServiceEvent()
        {
        }

        public IDisposable Register(Action callback) => Register(oneshot: false, callback);

        public IDisposable RegisterOneShot(Action callback) => Register(oneshot: true, callback);

#pragma warning disable CA1859
        // Use concrete types when possible for improved performance. Explicitly obscure any functionality other than a duspose token.
        private IDisposable Register(bool oneshot, Action callback)
#pragma warning restore CA1859
        {
            // Insert at the end of the list
            EventNode node = new(oneshot, callback);
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

    /// <summary>
    /// The service event with one parameter implementation
    /// </summary>
    public class ServiceEvent<T> : IServiceEvent<T>
    {
        private sealed class EventNode : LinkedListNode, IDisposable
        {
            private readonly Action<T> _callback;

            internal EventNode(bool oneshot, Action<T> callback)
            {
                if (oneshot)
                {
                    _callback = (T parameter) => {
                        callback(parameter);
                        Remove();
                    };
                }
                else
                {
                    _callback = callback;
                }
            }

            internal void Fire(T parameter)
            {
                _callback(parameter);
            }

            void IDisposable.Dispose()
            {
                Remove();
            }
        }

        private readonly LinkedListNode _events = new();

        public ServiceEvent()
        {
        }

        public IDisposable Register(Action<T> callback) => Register(oneshot: false, callback);

        public IDisposable RegisterOneShot(Action<T> callback) => Register(oneshot: true, callback);

#pragma warning disable CA1859
        // Use concrete types when possible for improved performance. Explicitly obscure any functionality other than a duspose token.
        private IDisposable Register(bool oneshot, Action<T> callback)
#pragma warning restore CA1859
        {
            // Insert at the end of the list
            EventNode node = new(oneshot, callback);
            _events.InsertBefore(node);
            return node;
        }

        public void Fire(T parameter)
        {
            foreach (EventNode node in _events.GetValues<EventNode>())
            {
                node.Fire(parameter);
            }
        }
    }
}

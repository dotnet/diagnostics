// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// Simple linked list implementation. It assumes the nodes in the list inherit this
    /// class. The standard C# LinkedList/LinkedListNode doesn't allow it to be inherited
    /// in the (i.e. ServiceEvent) nodes making it inefficient (because LinkedListNode
    /// contains the data node), difficult to remove/maintain the list because LinkedListNode
    /// doesn't have a remove function and LinkedList doesn't allow the nodes to be removed
    /// during enumeration.
    /// </summary>
    public class LinkedListNode
    {
        private LinkedListNode _previous;
        private LinkedListNode _next;

        /// <summary>
        /// Create a linked list node instance.
        /// </summary>
        public LinkedListNode()
        {
            _previous = this;
            _next = this;
        }

        /// <summary>
        /// Next node in the list
        /// </summary>
        public LinkedListNode Next => _next;

        /// <summary>
        /// Previous node in the list
        /// </summary>
        public LinkedListNode Previous => _previous;

        /// <summary>
        /// Cast to the data type. T must inherit from this class.
        /// </summary>
        /// <typeparam name="T">data node type</typeparam>
        /// <returns>T</returns>
        /// <exception cref="InvalidCastException">thrown if T mismatches the actual type that inherits this class</exception>
        public T Cast<T>()
            where T : LinkedListNode
        {
            return (T)this;
        }

        /// <summary>
        /// Insert the new node after before this one.
        /// </summary>
        /// <param name="node">node to add</param>
        public void InsertAfter(LinkedListNode node)
        {
            _next._previous = node;
            node._next = _next;
            node._previous = this;
            _next = node;
        }

        /// <summary>
        /// Insert the new node before this one.
        /// </summary>
        /// <param name="node">node to add</param>
        public void InsertBefore(LinkedListNode node)
        {
            _previous._next = node;
            node._previous = _previous;
            node._next = this;
            _previous = node;
        }

        /// <summary>
        /// Remove the this node from the list.
        /// </summary>
        public void Remove()
        {
            _previous._next = _next;
            _next._previous = _previous;
            _previous = this;
            _next = this;
        }

        /// <summary>
        /// Return forward enumerator for the linked list
        /// </summary>
        /// <typeparam name="T">node type</typeparam>
        /// <returns>enumerator</returns>
        public IEnumerable<T> GetValues<T>()
            where T : LinkedListNode
        {
            return new ForwardEnumerable<T>(this);
        }

        private class ForwardEnumerable<T> : IEnumerable<T>
            where T : LinkedListNode
        {
            private readonly LinkedListNode _list;

            internal ForwardEnumerable(LinkedListNode list)
            {
                _list = list;
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            IEnumerator<T> IEnumerable<T>.GetEnumerator()
            {
                return GetEnumerator();
            }

            private Enumerator GetEnumerator()
            {
                return new Enumerator(_list);
            }

            private class Enumerator : IEnumerator<T>
            {
                private readonly LinkedListNode _tail;
                private LinkedListNode _current;
                private LinkedListNode _next;

                internal Enumerator(LinkedListNode list)
                {
                    _tail = list;
                    Reset();
                }

                object IEnumerator.Current { get { return ((IEnumerator<T>)this).Current; } }

                T IEnumerator<T>.Current
                {
                    get { return _current.Cast<T>(); }
                }

                public bool MoveNext()
                {
                    if (_next == _tail)
                    {
                        return false;
                    }
                    _current = _next;
                    _next = _next.Next;
                    return true;
                }

                public void Reset()
                {
                    _current = _tail;
                    _next = _tail.Next;
                }

                public void Dispose()
                {
                }
            }
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.Runtime.DacInterface;
using Microsoft.Diagnostics.Runtime.Implementation;
using Microsoft.Diagnostics.Runtime.Interfaces;

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// Represents a managed thread in the target process.  Note this does not wrap purely native threads
    /// in the target process (that is, threads which have never run managed code before).
    /// </summary>
    public sealed class ClrThread : IClrThread, IEquatable<ClrThread>
    {
        private readonly IClrThreadHelpers _helpers;
        private readonly ulong _exceptionHandle;
        private ClrStackFrame[]? _frames;

        internal ClrThread(IClrThreadHelpers helpers, ClrRuntime runtime, ClrAppDomain? currentDomain, ulong address, in ThreadData data, bool isFinalizer, bool isGc)
        {
            _helpers = helpers;
            Runtime = runtime;
            Address = address;
            OSThreadId = data.OSThreadId;
            ManagedThreadId = (int)data.ManagedThreadId;
            CurrentAppDomain = currentDomain;
            LockCount = data.LockCount;
            State = (ClrThreadState)data.State;
            _exceptionHandle = data.LastThrownObjectHandle;
            IsFinalizer = isFinalizer;
            IsGc = isGc;

            if (data.Teb != 0)
            {
                IMemoryReader reader = _helpers.DataReader;
                uint pointerSize = (uint)reader.PointerSize;
                StackBase = reader.ReadPointer(data.Teb + pointerSize);
                StackLimit = reader.ReadPointer(data.Teb + pointerSize * 2);
            }
            GCMode = data.PreemptiveGCDisabled == 0 ? GCMode.Preemptive : GCMode.Cooperative;
        }

        /// <summary>
        /// Gets the runtime associated with this thread.
        /// </summary>
        public ClrRuntime Runtime { get; }

        IClrRuntime IClrThread.Runtime => Runtime;

        /// <summary>
        /// Gets the suspension state of the thread according to the runtime.
        /// </summary>
        public GCMode GCMode { get; }

        /// <summary>
        /// Gets the address of the underlying datastructure which makes up the Thread object.  This
        /// serves as a unique identifier.
        /// </summary>
        public ulong Address { get; }

        public ClrThreadState State { get; }

        /// <summary>
        /// Returns true if the thread is alive in the process, false if this thread was recently terminated.
        /// </summary>
        public bool IsAlive => OSThreadId != 0 && (State & (ClrThreadState.TS_Unstarted | ClrThreadState.TS_Dead)) == 0;

        /// <summary>
        /// Returns true if a finalizer thread otherwise false.
        /// </summary>
        public bool IsFinalizer { get; }

        /// <summary>
        /// Returns true if a GC thread otherwise false.
        /// </summary>
        public bool IsGc { get; }

        /// <summary>
        /// Gets the OS thread id for the thread.
        /// </summary>
        public uint OSThreadId { get; }

        /// <summary>
        /// Gets the managed thread ID (this is equivalent to <see cref="System.Threading.Thread.ManagedThreadId"/>
        /// in the target process).
        /// </summary>
        public int ManagedThreadId { get; }

        /// <summary>
        /// Gets the AppDomain the thread is running in.
        /// </summary>
        public ClrAppDomain? CurrentAppDomain { get; }

        IClrAppDomain? IClrThread.CurrentAppDomain => CurrentAppDomain;

        /// <summary>
        /// Gets the number of managed locks (Monitors) the thread has currently entered but not left.
        /// This will be highly inconsistent unless the process is stopped.
        /// </summary>
        public uint LockCount { get; }

        /// <summary>
        /// Gets the base of the stack for this thread, or 0 if the value could not be obtained.
        /// </summary>
        public ulong StackBase { get; }

        /// <summary>
        /// Gets the limit of the stack for this thread, or 0 if the value could not be obtained.
        /// </summary>
        public ulong StackLimit { get; }

        /// <summary>
        /// Enumerates the GC references (objects) on the stack.
        /// </summary>
        /// <returns>An enumeration of GC references on the stack as the GC sees them.</returns>
        public IEnumerable<ClrStackRoot> EnumerateStackRoots() => _helpers.EnumerateStackRoots(this);

        IEnumerable<IClrRoot> IClrThread.EnumerateStackRoots() => EnumerateStackRoots().Cast<IClrRoot>();

        /// <summary>
        /// Enumerates a stack trace for a given thread.  Note this method may loop infinitely in the case of
        /// stack corruption or other stack unwind issues which can happen in practice.  When enumerating frames
        /// out of this method you should be careful to either set a maximum loop count, or to ensure the stack
        /// unwind is making progress by ensuring that ClrStackFrame.StackPointer is making progress (though it
        /// is expected that sometimes two frames may return the same StackPointer in some corner cases).
        /// </summary>
        /// <returns>An enumeration of stack frames.</returns>
        public IEnumerable<ClrStackFrame> EnumerateStackTrace(bool includeContext = false)
        {
            if (_frames is not null)
            {
                if (!includeContext)
                {
                    return Array.AsReadOnly(_frames);
                }
                else
                {
                    // If context was requested, only enumerate the cached frames if they were created with a
                    // Context.  Since we don't store that as a variable, only return if any frame has a context
                    // set.
                    foreach (ClrStackFrame frame in _frames)
                    {
                        if (frame.Context.Length > 0)
                        {
                            return Array.AsReadOnly(_frames);
                        }
                    }
                }
            }

            _frames = _helpers.EnumerateStackTrace(this, includeContext).ToArray();
            return Array.AsReadOnly(_frames);
        }

        IEnumerable<IClrStackFrame> IClrThread.EnumerateStackTrace(bool includeContext) => EnumerateStackTrace(includeContext).Cast<IClrStackFrame>();

        public bool Equals(IClrThread? other)
        {
            return other is not null && other.Address == Address && other.OSThreadId == OSThreadId && other.ManagedThreadId == ManagedThreadId;
        }

        public bool Equals(ClrThread? other)
        {
            return other is not null && other.Address == Address && other.OSThreadId == OSThreadId && other.ManagedThreadId == ManagedThreadId;
        }

        public override bool Equals(object? obj)
        {
            if (obj is IClrThread thread)
                return thread.Equals(this);

            return false;
        }

        public override int GetHashCode() => Address.GetHashCode() ^ OSThreadId.GetHashCode() & ManagedThreadId.GetHashCode();

        /// <summary>
        /// Gets the exception currently on the thread.  Note that this field may be <see langword="null"/>.  Also note
        /// that this is basically the "last thrown exception", and may be stale...meaning the thread could
        /// be done processing the exception but a crash dump was taken before the current exception was
        /// cleared off the field.
        /// </summary>
        public ClrException? CurrentException
        {
            get
            {
                ulong ptr = _exceptionHandle;
                if (ptr == 0)
                    return null;

                ulong obj = _helpers.DataReader.ReadPointer(ptr);
                ClrException? ex = Runtime.Heap.GetExceptionObject(obj, this);
                return ex;
            }
        }

        IClrException? IClrThread.CurrentException => CurrentException;
    }
}

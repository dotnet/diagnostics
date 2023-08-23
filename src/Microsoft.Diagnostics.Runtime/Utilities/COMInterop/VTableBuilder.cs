// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    /// <summary>
    /// Builds an individual VTable for a COM object.
    /// </summary>
    [RequiresDynamicCode("This class uses reflection over delegates to generate code.  If used for NativeAOT, consider building a VTable with [UnmanagedCallersOnly] and native delegates instead.")]
    public sealed unsafe class VTableBuilder
    {
        private readonly Guid _guid;
        private readonly COMCallableIUnknown _wrapper;
        private readonly bool _forceValidation;
        private readonly List<Delegate> _delegates = new();

        private bool _complete;

        internal VTableBuilder(COMCallableIUnknown wrapper, Guid guid, bool forceValidation)
        {
            _guid = guid;
            _wrapper = wrapper;
            _forceValidation = forceValidation;
        }

        /// <summary>
        /// Adds a method to be the next function in the VTable.
        /// </summary>
        /// <param name="validate">Whether to validate the delegate matches requirements.</param>
        /// <param name="func">The function to add to the next slot of the VTable.</param>
        public void AddMethod(Delegate func, bool validate = false)
        {
            if (func is null)
                throw new ArgumentNullException(nameof(func));

            if (_complete)
                throw new InvalidOperationException();

            if (_forceValidation || validate)
            {
                if (func.Method.GetParameters().First().ParameterType != typeof(IntPtr))
                    throw new InvalidOperationException();
            }

            _delegates.Add(func);
        }

        /// <summary>
        /// Completes the VTable, registering its GUID with the associated COMCallableIUnknown's QueryInterface
        /// method.  Note that if this method is not called, then the COM interface will NOT be registered.
        /// </summary>
        /// <returns>A pointer to the interface built.  This pointer has not been AddRef'ed.</returns>
        public IntPtr Complete()
        {
            if (_complete)
                throw new InvalidOperationException();

            _complete = true;

            IntPtr obj = Marshal.AllocHGlobal(IntPtr.Size);

            int vtablePartSize = _delegates.Count * IntPtr.Size;
            IntPtr* vtable = (IntPtr*)Marshal.AllocHGlobal(vtablePartSize + sizeof(IUnknownVTable));
            *(void**)obj = vtable;

            IUnknownVTable iunk = _wrapper.IUnknown;
            *vtable++ = new IntPtr(iunk.QueryInterface);
            *vtable++ = new IntPtr(iunk.AddRef);
            *vtable++ = new IntPtr(iunk.Release);

            foreach (Delegate d in _delegates)
                *vtable++ = Marshal.GetFunctionPointerForDelegate(d);

            _wrapper.RegisterInterface(_guid, obj, _delegates);
            return obj;
        }
    }
}

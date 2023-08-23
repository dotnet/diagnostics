// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

#pragma warning disable CA1816 // Dispose methods should call SuppressFinalize
namespace Microsoft.Diagnostics.Runtime.Utilities
{
    public unsafe class CallableCOMWrapper : COMHelper, IDisposable
    {
        private bool _disposed;

        protected IntPtr Self { get; }
        private readonly IUnknownVTable* _unknownVTable;
        private readonly RefCountedFreeLibrary? _library;

        protected void* _vtable => _unknownVTable + 1;

        protected CallableCOMWrapper(CallableCOMWrapper toClone)
        {
            if (toClone is null)
                throw new ArgumentNullException(nameof(toClone));

            if (toClone._disposed)
                throw new ObjectDisposedException(GetType().FullName);

            Self = toClone.Self;
            _unknownVTable = toClone._unknownVTable;
            _library = toClone._library;

            AddRef();
            _library?.AddRef();
        }

        public int AddRef()
        {
            int count = _unknownVTable->AddRef(Self);
            return count;
        }

        public void SuppressRelease()
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        protected CallableCOMWrapper(RefCountedFreeLibrary? library, in Guid desiredInterface, IntPtr pUnknown)
        {
            _library = library;
            _library?.AddRef();

            IUnknownVTable* tbl = *(IUnknownVTable**)pUnknown;

            int hr = tbl->QueryInterface(pUnknown, desiredInterface, out IntPtr pCorrectUnknown);
            if (hr != 0)
            {
                GC.SuppressFinalize(this);
                throw new InvalidCastException($"{GetType().FullName}.QueryInterface({desiredInterface}) failed, hr=0x{hr:x}");
            }

            int count = tbl->Release(pUnknown);
            Self = pCorrectUnknown;
            _unknownVTable = *(IUnknownVTable**)pCorrectUnknown;
        }

        public int Release()
        {
            int count = _unknownVTable->Release(Self);
            return count;
        }

        public IntPtr QueryInterface(in Guid riid)
        {
            HResult hr = _unknownVTable->QueryInterface(Self, riid, out IntPtr unk);
            return hr.IsOK ? unk : IntPtr.Zero;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                Release();
                _library?.Release();
                _disposed = true;
            }
        }

        ~CallableCOMWrapper()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
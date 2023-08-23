// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if NET6_0_OR_GREATER
using System;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime.DbgEng;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    internal sealed unsafe class DacDataTargetCOM : ComWrappers
    {
        private static readonly ComInterfaceEntry* s_wrapperEntry = InitializeComInterfaceEntry();
        public static DacDataTargetCOM Instance { get; } = new();

        /// <summary>
        /// Returns a COM pointer to an IDacDataTarget
        /// </summary>
        public static IntPtr CreateIDacDataTarget(DacDataTarget dacData)
        {
            Guid dacDataTargetIID = DacDataTarget.IID_IDacDataTarget;

            IntPtr iUnk = Instance.GetOrCreateComInterfaceForObject(dacData, System.Runtime.InteropServices.CreateComInterfaceFlags.None);
            HResult result = Marshal.QueryInterface(iUnk, ref dacDataTargetIID, out IntPtr iDacDataTarget);
            Marshal.Release(iUnk);

            if (result)
                return iDacDataTarget;

            return IntPtr.Zero;
        }

        private static ComInterfaceEntry* InitializeComInterfaceEntry()
        {
            GetIUnknownImpl(out IntPtr qi, out IntPtr addRef, out IntPtr release);

            ComInterfaceEntry* wrappers = (ComInterfaceEntry*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(DacDataTargetCOM), sizeof(ComInterfaceEntry) * 3);
            wrappers[0].IID = DacDataTarget.IID_IDacDataTarget;
            wrappers[0].Vtable = IDacDataTargetVtbl.Create(qi, addRef, release);

            wrappers[1].IID = DacDataTarget.IID_IMetadataLocator;
            wrappers[1].Vtable = IMetaDataLocatorVtbl.Create(qi, addRef, release);

            wrappers[2].IID = DacDataTarget.IID_ICLRRuntimeLocator;
            wrappers[2].Vtable = ICLRRuntimeLocatorVtbl.Create(qi, addRef, release);

            return wrappers;
        }

        protected override unsafe ComInterfaceEntry* ComputeVtables(object obj, CreateComInterfaceFlags flags, out int count)
        {
            // We only expose ICLRRuntimeLocator if we actually have the base address of the runtime.
            DacDataTarget dacDataTarget = obj as DacDataTarget ?? throw new InvalidOperationException($"Expected {nameof(DacDataTarget)} but got {obj.GetType()}.");
            count = dacDataTarget.RuntimeBaseAddress == 0 ? 2 : 3;

            ComInterfaceEntry* result = s_wrapperEntry;
            return result;
        }

        protected override object? CreateObject(IntPtr externalComObject, CreateObjectFlags flags)
        {
            throw new NotImplementedException();
        }

        protected override void ReleaseObjects(IEnumerable objects)
        {
        }

        private static unsafe class IDacDataTargetVtbl
        {
            public static IntPtr Create(IntPtr qi, IntPtr addRef, IntPtr release)
            {
                IntPtr* vtblRaw = (IntPtr*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(ICLRRuntimeLocatorVtbl), IntPtr.Size * 14);
                vtblRaw[0] = qi;
                vtblRaw[1] = addRef;
                vtblRaw[2] = release;
                vtblRaw[3] = (IntPtr)(delegate* unmanaged<IntPtr, IMAGE_FILE_MACHINE*, int>)&GetMachineType;
                vtblRaw[4] = (IntPtr)(delegate* unmanaged<IntPtr, int*, int>)&GetPointerSize;
                vtblRaw[5] = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, ulong*, int>)&GetImageBase;
                vtblRaw[6] = (IntPtr)(delegate* unmanaged<IntPtr, ulong, IntPtr, int, int*, int>)&ReadVirtual;
                vtblRaw[7] = (IntPtr)(delegate* unmanaged<IntPtr, ulong, IntPtr, uint, uint*, int>)&WriteVirtual;
                vtblRaw[8] = (IntPtr)(delegate* unmanaged<IntPtr, uint, uint, ulong*, int>)&GetTLSValue;
                vtblRaw[9] = (IntPtr)(delegate* unmanaged<IntPtr, uint, uint, ulong, int>)&SetTLSValue;
                vtblRaw[10] = (IntPtr)(delegate* unmanaged<IntPtr, uint*, int>)&GetCurrentThreadID;
                vtblRaw[11] = (IntPtr)(delegate* unmanaged<IntPtr, uint, uint, int, IntPtr, int>)&GetThreadContext;
                vtblRaw[12] = (IntPtr)(delegate* unmanaged<IntPtr, uint, uint, IntPtr, int>)&SetThreadContext;
                vtblRaw[13] = (IntPtr)(delegate* unmanaged<IntPtr, uint, uint, IntPtr, IntPtr, IntPtr, int>)&Request;

                return (IntPtr)vtblRaw;
            }

            [UnmanagedCallersOnly]
            private static int GetMachineType(IntPtr self, IMAGE_FILE_MACHINE* pMachine)
            {
                DacDataTarget dacDataTarget = ComInterfaceDispatch.GetInstance<DacDataTarget>((ComInterfaceDispatch*)self);

                *pMachine = dacDataTarget.MachineType;
                return *pMachine != IMAGE_FILE_MACHINE.UNKNOWN ? HResult.S_OK : HResult.E_FAIL;
            }

            [UnmanagedCallersOnly]
            private static int GetPointerSize(IntPtr self, int* pSize)
            {
                DacDataTarget dacDataTarget = ComInterfaceDispatch.GetInstance<DacDataTarget>((ComInterfaceDispatch*)self);

                *pSize = dacDataTarget.PointerSize;
                return *pSize == 4 || *pSize == 8 ? HResult.S_OK : HResult.E_FAIL;
            }

            [UnmanagedCallersOnly]
            private static int GetImageBase(IntPtr self, IntPtr imagePathPtr, ulong* pBaseAddress)
            {
                DacDataTarget dacDataTarget = ComInterfaceDispatch.GetInstance<DacDataTarget>((ComInterfaceDispatch*)self);

                string? imagePath = Marshal.PtrToStringUni(imagePathPtr);
                if (imagePath is null)
                    return HResult.E_INVALIDARG;

                *pBaseAddress = dacDataTarget.GetImageBase(imagePath);
                return *pBaseAddress != 0 ? HResult.S_OK : HResult.E_FAIL;
            }

            [UnmanagedCallersOnly]
            private static int ReadVirtual(IntPtr self, ulong address, IntPtr buffer, int bytesRequested, int* pBytesRead)
            {
                DacDataTarget dacDataTarget = ComInterfaceDispatch.GetInstance<DacDataTarget>((ComInterfaceDispatch*)self);

                bool result = dacDataTarget.ReadVirtual(address, buffer, bytesRequested, out int bytesRead);
                *pBytesRead = bytesRead;
                return result ? HResult.S_OK : HResult.E_FAIL;
            }

            [UnmanagedCallersOnly]
            private static int WriteVirtual(IntPtr self, ulong address, IntPtr buffer, uint bytesRequested, uint* pBytesWritten)
            {
                *pBytesWritten = bytesRequested;
                return HResult.S_OK;
            }

            [UnmanagedCallersOnly]
            private static int GetTLSValue(IntPtr self, uint threadID, uint index, ulong* pValue)
            {
                *pValue = 0;
                return HResult.E_FAIL;
            }

            [UnmanagedCallersOnly]
            private static int SetTLSValue(IntPtr self, uint threadID, uint index, ulong value)
            {
                return HResult.E_FAIL;
            }

            [UnmanagedCallersOnly]
            private static int GetCurrentThreadID(IntPtr self, uint* pThreadID)
            {
                *pThreadID = 0;
                return HResult.E_FAIL;
            }

            [UnmanagedCallersOnly]
            private static int GetThreadContext(IntPtr self, uint threadID, uint contextFlags, int contextSize, IntPtr context)
            {
                DacDataTarget dacDataTarget = ComInterfaceDispatch.GetInstance<DacDataTarget>((ComInterfaceDispatch*)self);
                return dacDataTarget.GetThreadContext(threadID, contextFlags, contextSize, context) ? HResult.S_OK : HResult.E_FAIL;
            }

            [UnmanagedCallersOnly]
            private static int SetThreadContext(IntPtr self, uint threadID, uint contextSize, IntPtr context)
            {
                return HResult.S_OK;
            }

            [UnmanagedCallersOnly]
            private static int Request(IntPtr self, uint reqCode, uint inBufferSize, IntPtr inBuffer, IntPtr outBufferSize, IntPtr outBuffer)
            {
                return HResult.E_NOTIMPL;
            }
        }

        private static unsafe class IMetaDataLocatorVtbl
        {
            public static IntPtr Create(IntPtr qi, IntPtr addRef, IntPtr release)
            {
                IntPtr* vtblRaw = (IntPtr*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(ICLRRuntimeLocatorVtbl), IntPtr.Size * 4);
                vtblRaw[0] = qi;
                vtblRaw[1] = addRef;
                vtblRaw[2] = release;
                vtblRaw[3] = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, int, int, IntPtr, uint, uint, uint, IntPtr, int*, int>)&GetMetadata;

                return (IntPtr)vtblRaw;
            }

            [UnmanagedCallersOnly]
            private static int GetMetadata(IntPtr self, IntPtr fileNamePtr, int imageTimestamp, int imageSize,
                                                         IntPtr mvid, uint mdRva, uint flags, uint bufferSize, IntPtr buffer, int* dataSize)
            {
                DacDataTarget dacDataTarget = ComInterfaceDispatch.GetInstance<DacDataTarget>((ComInterfaceDispatch*)self);

                string? fileName = Marshal.PtrToStringUni(fileNamePtr);
                if (fileName is null)
                    return HResult.E_INVALIDARG;

                return dacDataTarget.GetMetadata(fileName, imageTimestamp, imageSize, mvid, mdRva, flags, bufferSize, buffer, dataSize);
            }
        }

        private static unsafe class ICLRRuntimeLocatorVtbl
        {
            public static IntPtr Create(IntPtr qi, IntPtr addRef, IntPtr release)
            {
                IntPtr* vtblRaw = (IntPtr*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(ICLRRuntimeLocatorVtbl), IntPtr.Size * 4);
                vtblRaw[0] = qi;
                vtblRaw[1] = addRef;
                vtblRaw[2] = release;
                vtblRaw[3] = (IntPtr)(delegate* unmanaged<IntPtr, ulong*, int>)&GetRuntimeBase;

                return (IntPtr)vtblRaw;
            }

            [UnmanagedCallersOnly]
            private static int GetRuntimeBase(IntPtr self, ulong* address)
            {
                DacDataTarget dacDataTarget = ComInterfaceDispatch.GetInstance<DacDataTarget>((ComInterfaceDispatch*)self);
                *address = dacDataTarget.RuntimeBaseAddress;
                return dacDataTarget.RuntimeBaseAddress != 0 ? HResult.S_OK : HResult.E_FAIL;
            }
        }
    }
}
#endif
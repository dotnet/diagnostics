// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime.DbgEng;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    [RequiresDynamicCode("Requires dynamic code generation to build interfaces.")]
    internal sealed unsafe class LegacyDacDataTargetWrapper : COMCallableIUnknown
    {
        private readonly DacDataTarget _dacDataTarget;

        public IntPtr IDacDataTarget { get; }

        public LegacyDacDataTargetWrapper(DacDataTarget dacDataTarget, bool implementRuntimeLocator)
        {
            _dacDataTarget = dacDataTarget;

            VTableBuilder builder = AddInterface(DacDataTarget.IID_IDacDataTarget, false);
            builder.AddMethod(new GetMachineTypeDelegate(GetMachineType));
            builder.AddMethod(new GetPointerSizeDelegate(GetPointerSize));
            builder.AddMethod(new GetImageBaseDelegate(GetImageBase));
            builder.AddMethod(new ReadVirtualDelegate(ReadVirtual));
            builder.AddMethod(new WriteVirtualDelegate(WriteVirtual));
            builder.AddMethod(new GetTLSValueDelegate(GetTLSValue));
            builder.AddMethod(new SetTLSValueDelegate(SetTLSValue));
            builder.AddMethod(new GetCurrentThreadIDDelegate(GetCurrentThreadID));
            builder.AddMethod(new GetThreadContextDelegate(GetThreadContext));
            builder.AddMethod(new SetThreadContextDelegate(SetThreadContext));
            builder.AddMethod(new RequestDelegate(Request));
            IDacDataTarget = builder.Complete();

            builder = AddInterface(DacDataTarget.IID_IMetadataLocator, false);
            builder.AddMethod(new GetMetadataDelegate(GetMetadata));
            builder.Complete();

            if (implementRuntimeLocator)
            {
                builder = AddInterface(DacDataTarget.IID_ICLRRuntimeLocator, false);
                builder.AddMethod(new GetRuntimeBaseDelegate(GetRuntimeBase));
                builder.Complete();
            }
        }

        private int GetMachineType(IntPtr self, out IMAGE_FILE_MACHINE machineType)
        {
            machineType = _dacDataTarget.MachineType;
            return machineType != IMAGE_FILE_MACHINE.UNKNOWN ? HResult.S_OK : HResult.E_FAIL;
        }

        private int GetPointerSize(IntPtr _, out int pointerSize)
        {
            pointerSize = _dacDataTarget.PointerSize;
            return pointerSize is 4 or 8 ? HResult.S_OK : HResult.E_FAIL;
        }

        private int GetImageBase(IntPtr _, string imagePath, out ulong baseAddress)
        {
            baseAddress = _dacDataTarget.GetImageBase(imagePath);
            return baseAddress != 0 ? HResult.S_OK : HResult.E_FAIL;
        }

        private int ReadVirtual(IntPtr _, ClrDataAddress cda, IntPtr buffer, int bytesRequested, out int bytesRead)
            => _dacDataTarget.ReadVirtual(cda, buffer, bytesRequested, out bytesRead) ? HResult.S_OK : HResult.E_FAIL;

        private int WriteVirtual(IntPtr self, ClrDataAddress address, IntPtr buffer, uint bytesRequested, out uint bytesWritten)
        {
            // This gets used by MemoryBarrier() calls in the dac, which really shouldn't matter what we do here.
            bytesWritten = bytesRequested;
            return HResult.S_OK;
        }

        private int GetTLSValue(IntPtr self, uint threadID, uint index, out ulong value)
        {
            value = 0;
            return HResult.E_FAIL;
        }

        private int SetTLSValue(IntPtr self, uint threadID, uint index, ClrDataAddress value)
        {
            return HResult.E_FAIL;
        }

        private int GetCurrentThreadID(IntPtr self, out uint threadID)
        {
            threadID = 0;
            return HResult.E_FAIL;
        }

        private int GetThreadContext(IntPtr self, uint threadID, uint contextFlags, int contextSize, IntPtr context)
            => _dacDataTarget.GetThreadContext(threadID, contextFlags, contextSize, context) ? HResult.S_OK : HResult.E_FAIL;

        private int SetThreadContext(IntPtr self, uint threadID, uint contextSize, IntPtr context) => HResult.S_OK;

        private int Request(IntPtr self, uint reqCode, uint inBufferSize, IntPtr inBuffer, IntPtr outBufferSize, ref IntPtr outBuffer) => HResult.E_NOTIMPL;

        private int GetMetadata(IntPtr self, string fileName, int imageTimestamp, int imageSize, IntPtr mvid, uint mdRva, uint flags, uint bufferSize, IntPtr buffer, int* pDataSize)
            => _dacDataTarget.GetMetadata(fileName, imageTimestamp, imageSize, mvid, mdRva, flags, bufferSize, buffer, pDataSize);

        private int GetRuntimeBase(IntPtr _, out ulong address)
        {
            address = _dacDataTarget.RuntimeBaseAddress;
            return address == 0 ? HResult.E_FAIL : HResult.S_OK;
        }

        private delegate int GetMetadataDelegate(IntPtr self, [In][MarshalAs(UnmanagedType.LPWStr)] string fileName, int imageTimestamp, int imageSize,
                                                     IntPtr mvid, uint mdRva, uint flags, uint bufferSize, IntPtr buffer, int* dataSize);
        private delegate int GetMachineTypeDelegate(IntPtr self, out IMAGE_FILE_MACHINE machineType);
        private delegate int GetPointerSizeDelegate(IntPtr self, out int pointerSize);
        private delegate int GetImageBaseDelegate(IntPtr self, [In][MarshalAs(UnmanagedType.LPWStr)] string imagePath, out ulong baseAddress);
        private delegate int ReadVirtualDelegate(IntPtr self, ClrDataAddress address, IntPtr buffer, int bytesRequested, out int bytesRead);
        private delegate int WriteVirtualDelegate(IntPtr self, ClrDataAddress address, IntPtr buffer, uint bytesRequested, out uint bytesWritten);
        private delegate int GetTLSValueDelegate(IntPtr self, uint threadID, uint index, out ulong value);
        private delegate int SetTLSValueDelegate(IntPtr self, uint threadID, uint index, ClrDataAddress value);
        private delegate int GetCurrentThreadIDDelegate(IntPtr self, out uint threadID);
        private delegate int GetThreadContextDelegate(IntPtr self, uint threadID, uint contextFlags, int contextSize, IntPtr context);
        private delegate int SetThreadContextDelegate(IntPtr self, uint threadID, uint contextSize, IntPtr context);
        private delegate int RequestDelegate(IntPtr self, uint reqCode, uint inBufferSize, IntPtr inBuffer, IntPtr outBufferSize, ref IntPtr outBuffer);

        private delegate int GetRuntimeBaseDelegate([In] IntPtr self, [Out] out ulong address);
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("88f7dfab-3ea7-4c3a-aefb-c4e8106173aa")]
    public interface IDebugDataSpaces
    {
        /* IDebugDataSpaces */

        [PreserveSig]
        int ReadVirtual(
            ulong Offset,
            [Out]
            IntPtr buffer,
            int BufferSize,
            out int BytesRead);

        [PreserveSig]
        int WriteVirtual(
            ulong Offset,
            [In][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)]
            byte[] buffer,
            uint BufferSize,
            out uint BytesWritten);

        [PreserveSig]
        int SearchVirtual(
            ulong Offset,
            ulong Length,
            [In][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)]
            byte[] pattern,
            uint PatternSize,
            uint PatternGranularity,
            out ulong MatchOffset);

        [PreserveSig]
        int ReadVirtualUncached(
            ulong Offset,
            [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)]
            byte[] buffer,
            uint BufferSize,
            out uint BytesRead);

        [PreserveSig]
        int WriteVirtualUncached(
            ulong Offset,
            [In][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)]
            byte[] buffer,
            uint BufferSize,
            out uint BytesWritten);

        [PreserveSig]
        int ReadPointersVirtual(
            uint Count,
            ulong Offset,
            [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)]
            ulong[] Ptrs);

        [PreserveSig]
        int WritePointersVirtual(
            uint Count,
            ulong Offset,
            [In][MarshalAs(UnmanagedType.LPArray)] ulong[] Ptrs);

        [PreserveSig]
        int ReadPhysical(
            ulong Offset,
            [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)]
            byte[] buffer,
            uint BufferSize,
            out uint BytesRead);

        [PreserveSig]
        int WritePhysical(
            ulong Offset,
            [In][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)]
            byte[] buffer,
            uint BufferSize,
            out uint BytesWritten);

        [PreserveSig]
        int ReadControl(
            uint Processor,
            ulong Offset,
            [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)]
            byte[] buffer,
            int BufferSize,
            out uint BytesRead);

        [PreserveSig]
        int WriteControl(
            uint Processor,
            ulong Offset,
            [In][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)]
            byte[] buffer,
            int BufferSize,
            out uint BytesWritten);

        [PreserveSig]
        int ReadIo(
            INTERFACE_TYPE InterfaceType,
            uint BusNumber,
            uint AddressSpace,
            ulong Offset,
            [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 5)]
            byte[] buffer,
            uint BufferSize,
            out uint BytesRead);

        [PreserveSig]
        int WriteIo(
            INTERFACE_TYPE InterfaceType,
            uint BusNumber,
            uint AddressSpace,
            ulong Offset,
            [In][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 5)]
            byte[] buffer,
            uint BufferSize,
            out uint BytesWritten);

        [PreserveSig]
        int ReadMsr(
            uint Msr,
            out ulong MsrValue);

        [PreserveSig]
        int WriteMsr(
            uint Msr,
            ulong MsrValue);

        [PreserveSig]
        int ReadBusData(
            BUS_DATA_TYPE BusDataType,
            uint BusNumber,
            uint SlotNumber,
            uint Offset,
            [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 5)]
            byte[] buffer,
            uint BufferSize,
            out uint BytesRead);

        [PreserveSig]
        int WriteBusData(
            BUS_DATA_TYPE BusDataType,
            uint BusNumber,
            uint SlotNumber,
            uint Offset,
            [In][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 5)]
            byte[] buffer,
            uint BufferSize,
            out uint BytesWritten);

        [PreserveSig]
        int CheckLowMemory();

        [PreserveSig]
        int ReadDebuggerData(
            uint Index,
            [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)]
            byte[] buffer,
            uint BufferSize,
            out uint DataSize);

        [PreserveSig]
        int ReadProcessorSystemData(
            uint Processor,
            DEBUG_DATA Index,
            [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)]
            byte[] buffer,
            uint BufferSize,
            out uint DataSize);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("88f7dfab-3ea7-4c3a-aefb-c4e8106173aa")]
    public interface IDebugDataSpacesPtr
    {
        /* IDebugDataSpaces */

        [PreserveSig]
        int ReadVirtual(
            ulong Offset,
            IntPtr buffer,
            uint BufferSize,
            out uint BytesRead);

        [PreserveSig]
        int WriteVirtual(
            ulong Offset,
            IntPtr buffer,
            uint BufferSize,
            out uint BytesWritten);

        [PreserveSig]
        int SearchVirtual(
            ulong Offset,
            ulong Length,
            IntPtr pattern,
            uint PatternSize,
            uint PatternGranularity,
            out ulong MatchOffset);

        [PreserveSig]
        int ReadVirtualUncached(
            ulong Offset,
            IntPtr buffer,
            uint BufferSize,
            out uint BytesRead);

        [PreserveSig]
        int WriteVirtualUncached(
            ulong Offset,
            IntPtr buffer,
            uint BufferSize,
            out uint BytesWritten);

        [PreserveSig]
        int ReadPointersVirtual(
            uint Count,
            ulong Offset,
            [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)]
            ulong[] Ptrs);

        [PreserveSig]
        int WritePointersVirtual(
            uint Count,
            ulong Offset,
            [In][MarshalAs(UnmanagedType.LPArray)] ulong[] Ptrs);

        [PreserveSig]
        int ReadPhysical(
            ulong Offset,
            IntPtr buffer,
            uint BufferSize,
            out uint BytesRead);

        [PreserveSig]
        int WritePhysical(
            ulong Offset,
            IntPtr buffer,
            uint BufferSize,
            out uint BytesWritten);

        [PreserveSig]
        int ReadControl(
            uint Processor,
            ulong Offset,
            IntPtr buffer,
            int BufferSize,
            out uint BytesRead);

        [PreserveSig]
        int WriteControl(
            uint Processor,
            ulong Offset,
            IntPtr buffer,
            int BufferSize,
            out uint BytesWritten);

        [PreserveSig]
        int ReadIo(
            INTERFACE_TYPE InterfaceType,
            uint BusNumber,
            uint AddressSpace,
            ulong Offset,
            IntPtr buffer,
            uint BufferSize,
            out uint BytesRead);

        [PreserveSig]
        int WriteIo(
            INTERFACE_TYPE InterfaceType,
            uint BusNumber,
            uint AddressSpace,
            ulong Offset,
            IntPtr buffer,
            uint BufferSize,
            out uint BytesWritten);

        [PreserveSig]
        int ReadMsr(
            uint Msr,
            out ulong MsrValue);

        [PreserveSig]
        int WriteMsr(
            uint Msr,
            ulong MsrValue);

        [PreserveSig]
        int ReadBusData(
            BUS_DATA_TYPE BusDataType,
            uint BusNumber,
            uint SlotNumber,
            uint Offset,
            IntPtr buffer,
            uint BufferSize,
            out uint BytesRead);

        [PreserveSig]
        int WriteBusData(
            BUS_DATA_TYPE BusDataType,
            uint BusNumber,
            uint SlotNumber,
            uint Offset,
            IntPtr buffer,
            uint BufferSize,
            out uint BytesWritten);

        [PreserveSig]
        int CheckLowMemory();

        [PreserveSig]
        int ReadDebuggerData(
            uint Index,
            IntPtr buffer,
            uint BufferSize,
            out uint DataSize);

        [PreserveSig]
        int ReadProcessorSystemData(
            uint Processor,
            DEBUG_DATA Index,
            IntPtr buffer,
            uint BufferSize,
            out uint DataSize);
    }
}
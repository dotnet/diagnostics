// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SOS.Hosting.DbgEng.Interop
{
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("d98ada1f-29e9-4ef5-a6c0-e53349883212")]
    public interface IDebugDataSpaces4 : IDebugDataSpaces3
    {
        /* IDebugDataSpaces */

        [PreserveSig]
        new int ReadVirtual(
            ulong Offset,
            [Out]
            IntPtr buffer,
            int BufferSize,
            out int BytesRead);

        [PreserveSig]
        new int WriteVirtual(
            ulong Offset,
            [In][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)]
            byte[] buffer,
            uint BufferSize,
            out uint BytesWritten);

        [PreserveSig]
        new int SearchVirtual(
            ulong Offset,
            ulong Length,
            [In][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)]
            byte[] pattern,
            uint PatternSize,
            uint PatternGranularity,
            out ulong MatchOffset);

        [PreserveSig]
        new int ReadVirtualUncached(
            ulong Offset,
            [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)]
            byte[] buffer,
            uint BufferSize,
            out uint BytesRead);

        [PreserveSig]
        new int WriteVirtualUncached(
            ulong Offset,
            [In][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)]
            byte[] buffer,
            uint BufferSize,
            out uint BytesWritten);

        [PreserveSig]
        new int ReadPointersVirtual(
            uint Count,
            ulong Offset,
            [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)]
            ulong[] Ptrs);

        [PreserveSig]
        new int WritePointersVirtual(
            uint Count,
            ulong Offset,
            [In][MarshalAs(UnmanagedType.LPArray)] ulong[] Ptrs);

        [PreserveSig]
        new int ReadPhysical(
            ulong Offset,
            [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)]
            byte[] buffer,
            uint BufferSize,
            out uint BytesRead);

        [PreserveSig]
        new int WritePhysical(
            ulong Offset,
            [In][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)]
            byte[] buffer,
            uint BufferSize,
            out uint BytesWritten);

        [PreserveSig]
        new int ReadControl(
            uint Processor,
            ulong Offset,
            [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)]
            byte[] buffer,
            int BufferSize,
            out uint BytesRead);

        [PreserveSig]
        new int WriteControl(
            uint Processor,
            ulong Offset,
            [In][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)]
            byte[] buffer,
            int BufferSize,
            out uint BytesWritten);

        [PreserveSig]
        new int ReadIo(
            INTERFACE_TYPE InterfaceType,
            uint BusNumber,
            uint AddressSpace,
            ulong Offset,
            [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 5)]
            byte[] buffer,
            uint BufferSize,
            out uint BytesRead);

        [PreserveSig]
        new int WriteIo(
            INTERFACE_TYPE InterfaceType,
            uint BusNumber,
            uint AddressSpace,
            ulong Offset,
            [In][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 5)]
            byte[] buffer,
            uint BufferSize,
            out uint BytesWritten);

        [PreserveSig]
        new int ReadMsr(
            uint Msr,
            out ulong MsrValue);

        [PreserveSig]
        new int WriteMsr(
            uint Msr,
            ulong MsrValue);

        [PreserveSig]
        new int ReadBusData(
            BUS_DATA_TYPE BusDataType,
            uint BusNumber,
            uint SlotNumber,
            uint Offset,
            [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 5)]
            byte[] buffer,
            uint BufferSize,
            out uint BytesRead);

        [PreserveSig]
        new int WriteBusData(
            BUS_DATA_TYPE BusDataType,
            uint BusNumber,
            uint SlotNumber,
            uint Offset,
            [In][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 5)]
            byte[] buffer,
            uint BufferSize,
            out uint BytesWritten);

        [PreserveSig]
        new int CheckLowMemory();

        [PreserveSig]
        new int ReadDebuggerData(
            uint Index,
            [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)]
            byte[] buffer,
            uint BufferSize,
            out uint DataSize);

        [PreserveSig]
        new int ReadProcessorSystemData(
            uint Processor,
            DEBUG_DATA Index,
            [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)]
            byte[] buffer,
            uint BufferSize,
            out uint DataSize);

        /* IDebugDataSpaces2 */

        [PreserveSig]
        new int VirtualToPhysical(
            ulong Virtual,
            out ulong Physical);

        [PreserveSig]
        new int GetVirtualTranslationPhysicalOffsets(
            ulong Virtual,
            [Out][MarshalAs(UnmanagedType.LPArray)]
            ulong[] Offsets,
            uint OffsetsSize,
            out uint Levels);

        [PreserveSig]
        new int ReadHandleData(
            ulong Handle,
            DEBUG_HANDLE_DATA_TYPE DataType,
            [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)]
            byte[] buffer,
            uint BufferSize,
            out uint DataSize);

        [PreserveSig]
        new int FillVirtual(
            ulong Start,
            uint Size,
            [In][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)]
            byte[] buffer,
            uint PatternSize,
            out uint Filled);

        [PreserveSig]
        new int FillPhysical(
            ulong Start,
            uint Size,
            [In][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)]
            byte[] buffer,
            uint PatternSize,
            out uint Filled);

        [PreserveSig]
        new int QueryVirtual(
            ulong Offset,
            out MEMORY_BASIC_INFORMATION64 Info);

        /* IDebugDataSpaces3 */

        [PreserveSig]
        new int ReadImageNtHeaders(
            ulong ImageBase,
            out IMAGE_NT_HEADERS64 Headers);

        [PreserveSig]
        new int ReadTagged(
            [In][MarshalAs(UnmanagedType.LPStruct)]
            Guid Tag,
            uint Offset,
            [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)]
            byte[] buffer,
            uint BufferSize,
            out uint TotalSize);

        [PreserveSig]
        new int StartEnumTagged(
            out ulong Handle);

        [PreserveSig]
        new int GetNextTagged(
            ulong Handle,
            out Guid Tag,
            out uint Size);

        [PreserveSig]
        new int EndEnumTagged(
            ulong Handle);

        /* IDebugDataSpaces4 */

        [PreserveSig]
        int GetOffsetInformation(
            DEBUG_DATA_SPACE Space,
            DEBUG_OFFSINFO Which,
            ulong Offset,
            [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)]
            byte[] buffer,
            uint BufferSize,
            out uint InfoSize);

        [PreserveSig]
        int GetNextDifferentlyValidOffsetVirtual(
            ulong Offset,
            out ulong NextOffset);

        [PreserveSig]
        int GetValidRegionVirtual(
            ulong Base,
            uint Size,
            out ulong ValidBase,
            out uint ValidSize);

        [PreserveSig]
        int SearchVirtual2(
            ulong Offset,
            ulong Length,
            DEBUG_VSEARCH Flags,
            [In][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)]
            byte[] buffer,
            uint PatternSize,
            uint PatternGranularity,
            out ulong MatchOffset);

        [PreserveSig]
        int ReadMultiByteStringVirtual(
            ulong Offset,
            uint MaxBytes,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            uint BufferSize,
            out uint StringBytes);

        [PreserveSig]
        int ReadMultiByteStringVirtualWide(
            ulong Offset,
            uint MaxBytes,
            CODE_PAGE CodePage,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            uint BufferSize,
            out uint StringBytes);

        [PreserveSig]
        int ReadUnicodeStringVirtual(
            ulong Offset,
            uint MaxBytes,
            CODE_PAGE CodePage,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder Buffer,
            uint BufferSize,
            out uint StringBytes);

        [PreserveSig]
        int ReadUnicodeStringVirtualWide(
            ulong Offset,
            uint MaxBytes,
            [Out][MarshalAs(UnmanagedType.LPWStr)] StringBuilder Buffer,
            uint BufferSize,
            out uint StringBytes);

        [PreserveSig]
        int ReadPhysical2(
            ulong Offset,
            DEBUG_PHYSICAL Flags,
            [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)]
            byte[] buffer,
            uint BufferSize,
            out uint BytesRead);

        [PreserveSig]
        int WritePhysical2(
            ulong Offset,
            DEBUG_PHYSICAL Flags,
            [In][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)]
            byte[] buffer,
            uint BufferSize,
            out uint BytesWritten);
    }
}
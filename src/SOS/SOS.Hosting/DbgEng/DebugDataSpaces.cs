// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime.Utilities;
using SOS.Hosting.DbgEng.Interop;

namespace SOS.Hosting.DbgEng
{
    internal unsafe class DebugDataSpaces
    {
        internal DebugDataSpaces(DebugClient client, SOSHost soshost)
        {
            VTableBuilder builder = client.AddInterface(typeof(IDebugDataSpaces).GUID, validate: true);
            AddDebugDataSpaces(builder, soshost);
            builder.Complete();

            builder = client.AddInterface(typeof(IDebugDataSpaces2).GUID, validate: true);
            AddDebugDataSpaces2(builder, soshost);
            builder.Complete();
        }

        private static void AddDebugDataSpaces(VTableBuilder builder, SOSHost soshost)
        {
            builder.AddMethod(new ReadVirtualDelegate(soshost.ReadVirtual));
            builder.AddMethod(new WriteVirtualDelegate(soshost.WriteVirtual));
            builder.AddMethod(new SearchVirtualDelegate((self, offset, length, pattern, patternSize, patternGranularity, matchOffset) => DebugClient.NotImplemented));
            builder.AddMethod(new ReadVirtualUncachedDelegate((self, offset, buffer, bufferSize, bytesRead) => DebugClient.NotImplemented));
            builder.AddMethod(new WriteVirtualUncachedDelegate((self, offset, buffer, bufferSize, bytesWritten) => DebugClient.NotImplemented));
            builder.AddMethod(new ReadPointersVirtualDelegate((self, count, offset, ptrs) => DebugClient.NotImplemented));
            builder.AddMethod(new WritePointersVirtualDelegate((self, count, offset, ptrs) => DebugClient.NotImplemented));
            builder.AddMethod(new ReadPhysicalDelegate((self, offset, buffer, bufferSize, bytesRead) => DebugClient.NotImplemented));
            builder.AddMethod(new WritePhysicalDelegate((self, offset, buffer, bufferSize, bytesWritten) => DebugClient.NotImplemented));
            builder.AddMethod(new ReadControlDelegate((self, processor, offset, buffer, bufferSize, bytesRead) => DebugClient.NotImplemented));
            builder.AddMethod(new WriteControlDelegate((self, processor, offset, buffer, bufferSize, bytesWritten) => DebugClient.NotImplemented));
            builder.AddMethod(new ReadIoDelegate((self, interfaceType, busNumber, addressSpace, offset, buffer, bufferSize, bytesRead) => DebugClient.NotImplemented));
            builder.AddMethod(new WriteIoDelegate((self, interfaceType, busNumber, addressSpace, offset, buffer, bufferSize, bytesWritten) => DebugClient.NotImplemented));
            builder.AddMethod(new ReadMsrDelegate((self, msr, msrValue) => DebugClient.NotImplemented));
            builder.AddMethod(new WriteMsrDelegate((self, msr, msrValue) => DebugClient.NotImplemented));
            builder.AddMethod(new ReadBusDataDelegate((self, busDataType, busNumber, slotNumber, offset, buffer, bufferSize, bytesRead) => DebugClient.NotImplemented));
            builder.AddMethod(new WriteBusDataDelegate((self, busDataType, busNumber, slotNumber, offset, buffer, bufferSize, bytesWritten) => DebugClient.NotImplemented));
            builder.AddMethod(new CheckLowMemoryDelegate((self) => DebugClient.NotImplemented));
            builder.AddMethod(new ReadDebuggerDataDelegate((self, index, buffer, bufferSize, dataSize) => HResult.E_NOTIMPL));
            builder.AddMethod(new ReadProcessorSystemDataDelegate((self, processor, index, buffer, bufferSize, dataSize) => DebugClient.NotImplemented));
        }

        private static void AddDebugDataSpaces2(VTableBuilder builder, SOSHost soshost)
        {
            AddDebugDataSpaces(builder, soshost);
            builder.AddMethod(new VirtualToPhysicalDelegate((self, virtualAddress, physicalAddress) => DebugClient.NotImplemented));
            builder.AddMethod(new GetVirtualTranslationPhysicalOffsetsDelegate((self, virtualAddress, offsets, offsetsSize, levels) => DebugClient.NotImplemented));
            builder.AddMethod(new ReadHandleDataDelegate((self, handle, dataType, buffer, bufferSize, dataSize) => DebugClient.NotImplemented));
            builder.AddMethod(new FillVirtualDelegate((self, start, size, buffer, patternSize, filled) => DebugClient.NotImplemented));
            builder.AddMethod(new FillPhysicalDelegate((self, start, size, buffer, patternSize, filled) => DebugClient.NotImplemented));
            builder.AddMethod(new QueryVirtualDelegate((self, offset, info) => DebugClient.NotImplemented));
        }

        #region IDebugDataSpaces Delegates

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private unsafe delegate int ReadVirtualDelegate(
            IntPtr self,
            [In] ulong address,
            IntPtr buffer,
            [In] uint bufferSize,
            [Out] uint* bytesRead);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int WriteVirtualDelegate(
            IntPtr self,
            [In] ulong address,
            IntPtr buffer,
            [In] uint bufferSize,
            [Out] uint* bytesWritten);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SearchVirtualDelegate(
            IntPtr self,
            [In] ulong Offset,
            [In] ulong Length,
            [In] byte* pattern,
            [In] uint PatternSize,
            [In] uint PatternGranularity,
            [Out] ulong* MatchOffset);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ReadVirtualUncachedDelegate(
            IntPtr self,
            [In] ulong Offset,
            [Out] byte* buffer,
            [In] uint BufferSize,
            [Out] uint* BytesRead);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int WriteVirtualUncachedDelegate(
            IntPtr self,
            [In] ulong Offset,
            [In] byte* buffer,
            [In] uint BufferSize,
            [Out] uint* BytesWritten);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ReadPointersVirtualDelegate(
            IntPtr self,
            [In] uint Count,
            [In] ulong Offset,
            [Out] ulong* Ptrs);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int WritePointersVirtualDelegate(
            IntPtr self,
            [In] uint Count,
            [In] ulong Offset,
            [In] ulong[] Ptrs);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ReadPhysicalDelegate(
            IntPtr self,
            [In] ulong Offset,
            [Out] byte* buffer,
            [In] uint BufferSize,
            [Out] uint* BytesRead);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int WritePhysicalDelegate(
            IntPtr self,
            [In] ulong Offset,
            [In] byte* buffer,
            [In] uint BufferSize,
            [Out] uint* BytesWritten);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ReadControlDelegate(
            IntPtr self,
            [In] uint Processor,
            [In] ulong Offset,
            [Out] byte* buffer,
            [In] int BufferSize,
            [Out] uint* BytesRead);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int WriteControlDelegate(
            IntPtr self,
            [In] uint Processor,
            [In] ulong Offset,
            [In] byte* buffer,
            [In] int BufferSize,
            [Out] uint* BytesWritten);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ReadIoDelegate(
            IntPtr self,
            [In] INTERFACE_TYPE InterfaceType,
            [In] uint BusNumber,
            [In] uint AddressSpace,
            [In] ulong Offset,
            [Out] byte* buffer,
            [In] uint BufferSize,
            [Out] uint* BytesRead);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int WriteIoDelegate(
            IntPtr self,
            [In] INTERFACE_TYPE InterfaceType,
            [In] uint BusNumber,
            [In] uint AddressSpace,
            [In] ulong Offset,
            [In] byte* buffer,
            [In] uint BufferSize,
            [Out] uint* BytesWritten);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ReadMsrDelegate(
            IntPtr self,
            [In] uint Msr,
            [Out] ulong* MsrValue);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int WriteMsrDelegate(
            IntPtr self,
            [In] uint Msr,
            [In] ulong MsrValue);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ReadBusDataDelegate(
            IntPtr self,
            [In] BUS_DATA_TYPE BusDataType,
            [In] uint BusNumber,
            [In] uint SlotNumber,
            [In] uint Offset,
            [Out] byte* buffer,
            [In] uint BufferSize,
            [Out] uint* BytesRead);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int WriteBusDataDelegate(
            IntPtr self,
            [In] BUS_DATA_TYPE BusDataType,
            [In] uint BusNumber,
            [In] uint SlotNumber,
            [In] uint Offset,
            [In] byte* buffer,
            [In] uint BufferSize,
            [Out] uint* BytesWritten);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int CheckLowMemoryDelegate(
            IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ReadDebuggerDataDelegate(
            IntPtr self,
            [In] uint Index,
            [Out] byte* buffer,
            [In] uint BufferSize,
            [Out] uint* DataSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ReadProcessorSystemDataDelegate(
            IntPtr self,
            [In] uint Processor,
            [In] DEBUG_DATA Index,
            [Out] byte* buffer,
            [In] uint BufferSize,
            [Out] uint* DataSize);

        #endregion

        #region IDebugDataSpaces2 Delegates

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int VirtualToPhysicalDelegate(
            IntPtr self,
            [In] ulong Virtual,
            [Out] ulong* Physical);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetVirtualTranslationPhysicalOffsetsDelegate(
            IntPtr self,
            [In] ulong Virtual,
            [Out] ulong* Offsets,
            [In] uint OffsetsSize,
            [Out] uint* Levels);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ReadHandleDataDelegate(
            IntPtr self,
            [In] ulong Handle,
            [In] DEBUG_HANDLE_DATA_TYPE DataType,
            [Out] byte* buffer,
            [In] uint BufferSize,
            [Out] uint* DataSize);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int FillVirtualDelegate(
            IntPtr self,
            [In] ulong Start,
            [In] uint Size,
            [In] byte* buffer,
            [In] uint PatternSize,
            [Out] uint* Filled);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int FillPhysicalDelegate(
            IntPtr self,
            [In] ulong Start,
            [In] uint Size,
            [In] byte* buffer,
            [In] uint PatternSize,
            [Out] uint* Filled);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int QueryVirtualDelegate(
            IntPtr self,
            [In] ulong Offset,
            [Out] MEMORY_BASIC_INFORMATION64* Info);

        #endregion
    }
}

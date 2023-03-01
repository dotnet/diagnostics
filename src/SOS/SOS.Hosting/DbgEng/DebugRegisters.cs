// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Diagnostics.Runtime.Utilities;
using SOS.Hosting.DbgEng.Interop;

namespace SOS.Hosting.DbgEng
{
    internal sealed unsafe class DebugRegisters
    {
        internal DebugRegisters(DebugClient client, SOSHost soshost)
        {
            VTableBuilder builder = client.AddInterface(typeof(IDebugRegisters).GUID, validate: true);
            AddDebugRegisters(builder, soshost);
            builder.Complete();
        }

        private static void AddDebugRegisters(VTableBuilder builder, SOSHost soshost)
        {
            builder.AddMethod(new GetNumberRegistersDelegate((self, number) => DebugClient.NotImplemented));
            builder.AddMethod(new GetDescriptionDelegate((self, register, nameBuffer, nameBufferSize, nameSize, desc) => DebugClient.NotImplemented));
            builder.AddMethod(new GetIndexByNameDelegate(soshost.GetIndexByName));
            builder.AddMethod(new GetValueDelegate(soshost.GetValue));
            builder.AddMethod(new SetValueDelegate((self, register, value) => DebugClient.NotImplemented));
            builder.AddMethod(new GetValuesDelegate((self, count, indices, start, values) => DebugClient.NotImplemented));
            builder.AddMethod(new SetValuesDelegate((self, count, indices, start, values) => DebugClient.NotImplemented));
            builder.AddMethod(new OutputRegistersDelegate((self, outputControl, flags) => DebugClient.NotImplemented));
            builder.AddMethod(new GetInstructionOffsetDelegate(soshost.GetInstructionOffset));
            builder.AddMethod(new GetStackOffsetDelegate(soshost.GetStackOffset));
            builder.AddMethod(new GetFrameOffsetDelegate(soshost.GetFrameOffset));
        }

        #region IDebugRegisters Delegates

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetNumberRegistersDelegate(
            IntPtr self,
            [Out] uint* Number);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetDescriptionDelegate(
            IntPtr self,
            [In] uint Register,
            [Out][MarshalAs(UnmanagedType.LPStr)] StringBuilder NameBuffer,
            [In] int NameBufferSize,
            [Out] uint* NameSize,
            [Out] DEBUG_REGISTER_DESCRIPTION* Desc);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetIndexByNameDelegate(
            IntPtr self,
            [In][MarshalAs(UnmanagedType.LPStr)] string Name,
            [Out] out uint Index);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetValueDelegate(
            IntPtr self,
            [In] uint Register,
            [Out] out DEBUG_VALUE Value);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetValueDelegate(
            IntPtr self,
            [In] uint Register,
            [In] DEBUG_VALUE* Value);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetValuesDelegate( //FIX ME!!! This needs to be tested
            IntPtr self,
            [In] uint Count,
            [In] uint* Indices,
            [In] uint Start,
            [Out] DEBUG_VALUE* Values);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SetValuesDelegate(
            IntPtr self,
            [In] uint Count,
            [In] uint* Indices,
            [In] uint Start,
            [In] DEBUG_VALUE* Values);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int OutputRegistersDelegate(
            IntPtr self,
            [In] DEBUG_OUTCTL OutputControl,
            [In] DEBUG_REGISTERS Flags);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetInstructionOffsetDelegate(
            IntPtr self,
            [Out] out ulong Offset);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetStackOffsetDelegate(
            IntPtr self,
            [Out] out ulong Offset);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetFrameOffsetDelegate(
            IntPtr self,
            [Out] out ulong Offset);

        #endregion
    }
}

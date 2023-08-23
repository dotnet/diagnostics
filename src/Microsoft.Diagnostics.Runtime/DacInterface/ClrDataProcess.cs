// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    /// <summary>
    /// This is an undocumented, untested, and unsupported interface.  Do not use.
    /// </summary>
    internal sealed unsafe class ClrDataProcess : CallableCOMWrapper
    {
        private static readonly Guid IID_IXCLRDataProcess = new("5c552ab6-fc09-4cb3-8e36-22fa03c798b7");
        private readonly DacLibrary _library;

        public ClrDataProcess(DacLibrary library, IntPtr pUnknown)
            : base(library?.OwningLibrary, IID_IXCLRDataProcess, pUnknown)
        {
            if (library is null)
                throw new ArgumentNullException(nameof(library));

            _library = library;
        }

        private ref readonly IXCLRDataProcessVTable VTable => ref Unsafe.AsRef<IXCLRDataProcessVTable>(_vtable);

        public ClrDataProcess(DacLibrary library, CallableCOMWrapper toClone) : base(toClone)
        {
            _library = library;
        }

        public SOSDac? GetSOSDacInterface()
        {
            IntPtr result = QueryInterface(SOSDac.IID_ISOSDac);
            if (result == IntPtr.Zero)
                return null;

            try
            {
                return new SOSDac(_library, result);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        public SOSDac6? GetSOSDacInterface6()
        {
            IntPtr result = QueryInterface(SOSDac6.IID_ISOSDac6);
            if (result == IntPtr.Zero)
                return null;

            try
            {
                return new SOSDac6(_library, result);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        public SOSDac8? GetSOSDacInterface8()
        {
            IntPtr result = QueryInterface(SOSDac8.IID_ISOSDac8);
            if (result == IntPtr.Zero)
                return null;

            try
            {
                return new SOSDac8(_library, result);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        public SosDac12? GetSOSDacInterface12()
        {
            IntPtr result = QueryInterface(SosDac12.IID_ISOSDac12);
            if (result == IntPtr.Zero)
                return null;

            try
            {
                return new SosDac12(_library, result);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        public ISOSDac13? GetSOSDacInterface13()
        {
            IntPtr result = QueryInterface(SOSDac13.IID_ISOSDac13);
            if (result != IntPtr.Zero)
            {
                try
                {
                    return new SOSDac13(_library, result);
                }
                catch (InvalidOperationException)
                {
                }
            }

            result = QueryInterface(SOSDac13Old.IID_ISOSDac13);
            if (result != IntPtr.Zero)
            {
                try
                {
                    return new SOSDac13Old(_library, result);
                }
                catch (InvalidOperationException)
                {
                }
            }

            return null;
        }

        public void Flush()
        {
            VTable.Flush(Self);
        }

        public HResult Request(uint reqCode, ReadOnlySpan<byte> input, Span<byte> output)
        {
            fixed (byte* pInput = input)
            fixed (byte* pOutput = output)
                return VTable.Request(Self, reqCode, input.Length, pInput, output.Length, pOutput);
        }

        public ClrStackWalk? CreateStackWalk(uint id, uint flags)
        {
            HResult hr = VTable.GetTaskByOSThreadID(Self, id, out IntPtr pUnkTask);
            if (!hr)
            {
                Trace.TraceInformation($"GetTaskByOSThreadID failed - id:{id:x} flags:{flags:x} hr:{hr}");
                return null;
            }

            using ClrDataTask dataTask = new(_library, pUnkTask);
            // There's a bug in certain runtimes where we will fail to release data deep in the runtime
            // when a C++ exception occurs while constructing a ClrDataStackWalk.  This is a workaround
            // for the largest of the leaks caused by this issue.
            //     https://github.com/Microsoft/clrmd/issues/47
            int count = AddRef();
            ClrStackWalk? res = dataTask.CreateStackWalk(_library, flags);
            int released = Release();
            if (released == count && res is null)
                Release();
            return res;
        }

        public IEnumerable<ClrDataMethod> EnumerateMethodInstancesByAddress(ClrDataAddress addr)
        {
            List<ClrDataMethod> result = new(1);

            HResult hr = VTable.StartEnumMethodInstancesByAddress(Self, addr, IntPtr.Zero, out ClrDataAddress handle);
            if (!hr)
                return result;

            try
            {
                while (VTable.EnumMethodInstanceByAddress(Self, ref handle, out IntPtr method) == HResult.S_OK)
                    result.Add(new ClrDataMethod(_library, method));
            }
            finally
            {
                VTable.EndEnumMethodInstancesByAddress(Self, handle);
            }

            return result;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly unsafe struct IXCLRDataProcessVTable
    {
        public readonly delegate* unmanaged[Stdcall]<IntPtr, int> Flush;
        private readonly IntPtr Unused_StartEnumTasks;
        private readonly IntPtr EnumTask;
        private readonly IntPtr EndEnumTasks;

        // (uint id, [Out, MarshalAs(UnmanagedType.IUnknown)] out object task);
        public readonly delegate* unmanaged[Stdcall]<IntPtr, uint, out IntPtr, int> GetTaskByOSThreadID;

        private readonly IntPtr GetTaskByUniqueID;
        private readonly IntPtr GetFlags;
        private readonly IntPtr IsSameObject;
        private readonly IntPtr GetManagedObject;
        private readonly IntPtr GetDesiredExecutionState;
        private readonly IntPtr SetDesiredExecutionState;
        private readonly IntPtr GetAddressType;
        private readonly IntPtr GetRuntimeNameByAddress;
        private readonly IntPtr StartEnumAppDomains;
        private readonly IntPtr EnumAppDomain;
        private readonly IntPtr EndEnumAppDomains;
        private readonly IntPtr GetAppDomainByUniqueID;
        private readonly IntPtr StartEnumAssemblie;
        private readonly IntPtr EnumAssembly;
        private readonly IntPtr EndEnumAssemblies;
        private readonly IntPtr StartEnumModules;
        private readonly IntPtr EnumModule;
        private readonly IntPtr EndEnumModules;
        private readonly IntPtr GetModuleByAddress;

        // (ulong address, [In, MarshalAs(UnmanagedType.Interface)] object appDomain, out ulong handle);
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, IntPtr, out ClrDataAddress, int> StartEnumMethodInstancesByAddress;

        // (ref ulong handle, [Out, MarshalAs(UnmanagedType.Interface)] out object method);
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ref ClrDataAddress, out IntPtr, int> EnumMethodInstanceByAddress;

        // (ulong handle);
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, int> EndEnumMethodInstancesByAddress;
        private readonly IntPtr GetDataByAddress;
        private readonly IntPtr GetExceptionStateByExceptionRecord;
        private readonly IntPtr TranslateExceptionRecordToNotification;

        // (uint reqCode, uint inBufferSize, [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] inBuffer, uint outBufferSize, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] byte[] outBuffer);
        public readonly delegate* unmanaged[Stdcall]<IntPtr, uint, int, byte*, int, byte*, int> Request;
    }
}
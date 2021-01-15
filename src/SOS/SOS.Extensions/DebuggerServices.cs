﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Interop;
using Microsoft.Diagnostics.Runtime.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace SOS
{
    internal unsafe class DebuggerServices : CallableCOMWrapper
    {
        internal enum OperatingSystem
        {
            Unknown = 0,
            Windows = 1,
            Linux = 2,
            OSX = 3,
        };

        internal static Guid IID_IDebuggerServices = new Guid("B4640016-6CA0-468E-BA2C-1FFF28DE7B72");

        private ref readonly IDebuggerServicesVTable VTable => ref Unsafe.AsRef<IDebuggerServicesVTable >(_vtable);

        internal DebuggerServices(IntPtr punk)
            : base(new RefCountedFreeLibrary(IntPtr.Zero), IID_IDebuggerServices, punk)
        {
        }

        public HResult GetOperatingSystem(out DebuggerServices.OperatingSystem operatingSystem)
        {
            return VTable.GetOperatingSystem(Self, out operatingSystem);
        }

        public HResult GetDebuggeeType(out DEBUG_CLASS debugClass, out DEBUG_CLASS_QUALIFIER qualifier)
        {
            return VTable.GetDebuggeeType(Self, out debugClass, out qualifier);
        }

        public HResult GetExecutingProcessorType(out IMAGE_FILE_MACHINE type)
        {
            return VTable.GetExecutingProcessorType(Self, out type);
        }

        public HResult AddCommand(string command, string help, IEnumerable<string> aliases)
        {
            if (string.IsNullOrEmpty(command) || string.IsNullOrEmpty(help) || aliases == null) throw new ArgumentNullException();

            byte[] commandBytes = Encoding.ASCII.GetBytes(command + "\0");
            byte[] helpBytes = Encoding.ASCII.GetBytes(help + "\0");
            IntPtr[] aliasHandles = aliases.Select((alias) => Marshal.StringToHGlobalAnsi(alias)).ToArray();
            try
            {
                fixed (byte* commandPtr = commandBytes)
                fixed (byte* helpPtr = helpBytes)
                fixed (IntPtr* aliasesPtr = aliasHandles)
                {
                    return VTable.AddCommand(Self, commandPtr, helpPtr, aliasesPtr, aliasHandles.Length);
                }
            }
            finally
            {
                foreach (IntPtr handle in aliasHandles)
                {
                    Marshal.FreeHGlobal(handle);
                }
            }
        }

        public void OutputString(DEBUG_OUTPUT mask, string message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            byte[] messageBytes = Encoding.ASCII.GetBytes(message + "\0");
            fixed (byte* messagePtr = messageBytes)
            {
                VTable.OutputString(Self, mask, messagePtr);
            }
        }

        public HResult ReadVirtual(ulong offset, Span<byte> buffer, out int bytesRead)
        {
            fixed (byte* bufferPtr = buffer)
            {
                return VTable.ReadVirtual(Self, offset, bufferPtr, (uint)buffer.Length, out bytesRead);
            }
        }

        public HResult WriteVirtual(ulong offset, Span<byte> buffer, out int bytesWritten)
        {
            fixed (byte* bufferPtr = buffer)
            {
                return VTable.WriteVirtual(Self, offset, bufferPtr, (uint)buffer.Length, out bytesWritten);
            }
        }

        public HResult GetNumberModules(out uint loaded, out uint unloaded)
        {
            return VTable.GetNumberModules(Self, out loaded, out unloaded);
        }

        public HResult GetModuleName(int index, out string imageName)
        {
            imageName = null;

            byte[] imageNameBuffer = new byte[1024];
            fixed (byte* imageNameBufferPtr = imageNameBuffer)
            {
                HResult hr = VTable.GetModuleNames(
                    Self,
                    (uint)index,
                    0,
                    imageNameBufferPtr,
                    (uint)imageNameBuffer.Length,
                    out uint imageNameSize,
                    null,
                    0,
                    null,
                    null,
                    0,
                    null);

                if (hr >= HResult.S_OK)
                {
                    if (imageNameSize > 0)
                    {
                        imageName = Encoding.ASCII.GetString(imageNameBufferPtr, (int)imageNameSize - 1);
                    }
                    else
                    {
                        hr = HResult.E_INVALIDARG;
                    }
                }
                return hr;
            }
        }

        public HResult GetModuleInfo(int index, out ulong moduleBase, out ulong moduleSize, out uint timestamp, out uint checksum)
        {
            return VTable.GetModuleInfo(Self, (uint)index, out moduleBase, out moduleSize, out timestamp, out checksum);
        }

        private static readonly byte[] s_getVersionInfo = Encoding.ASCII.GetBytes("\\\0");

        public HResult GetModuleVersionInformation(int index, out VS_FIXEDFILEINFO fileInfo)
        {
            int versionBufferSize = Marshal.SizeOf(typeof(VS_FIXEDFILEINFO));
            byte[] versionBuffer = new byte[versionBufferSize];
            fileInfo = default;

            fixed (byte* getVersionInfoPtr = s_getVersionInfo)
            fixed (byte* versionBufferPtr = versionBuffer)
            {
                HResult hr = VTable.GetModuleVersionInformation(Self, (uint)index, 0, getVersionInfoPtr, versionBufferPtr, (uint)versionBufferSize, null);
                if (hr == HResult.S_OK) {
                    fileInfo = *((VS_FIXEDFILEINFO*)versionBufferPtr);
                }
                return hr;
            }
        }

        private static readonly byte[] s_getVersionString = Encoding.ASCII.GetBytes("\\StringFileInfo\\040904B0\\FileVersion\0");

        public HResult GetModuleVersionString(int index, out string version)
        {
            byte[] versionBuffer = new byte[1024];
            version = default;

            fixed (byte* getVersionStringPtr = s_getVersionString)
            fixed (byte* versionBufferPtr = versionBuffer)
            {
                int hr = VTable.GetModuleVersionInformation(Self, (uint)index, 0, getVersionStringPtr, versionBufferPtr, (uint)versionBuffer.Length, null);
                if (hr == HResult.S_OK) {
                    version = Marshal.PtrToStringAnsi(new IntPtr(versionBufferPtr));
                }
                return hr;
            }
        }

        public HResult GetNumberThreads(out uint number)
        {
            return VTable.GetNumberThreads(Self, out number);
        }

        public HResult GetThreadIdsByIndex(uint start, uint count, uint[] ids, uint[] sysIds)
        {
            if (ids != null && (start >= ids.Length || start + count > ids.Length)) throw new ArgumentOutOfRangeException(nameof(ids));
            if (sysIds != null && (start >= sysIds.Length || start + count > sysIds.Length)) throw new ArgumentOutOfRangeException(nameof(sysIds));

            fixed (uint* pids = ids)
            {
                fixed (uint* psysIds = sysIds)
                {
                    return VTable.GetThreadIdsByIndex(Self, start, count, pids, psysIds);
                }
            }
        }

        public HResult GetThreadContext(uint threadId, uint contextFlags, uint contextSize, byte[] context)
        {
            fixed (byte* contextPtr = context)
            {
                return VTable.GetThreadContextBySystemId(Self, threadId, contextFlags, contextSize, contextPtr);
            }
        }

        public HResult GetCurrentProcessId(out uint processId)
        {
            return VTable.GetCurrentProcessSystemId(Self, out processId);
        }

        public HResult GetCurrentThreadId(out uint threadId)
        {
            return VTable.GetCurrentThreadSystemId(Self, out threadId);
        }

        public HResult SetCurrentThreadId(uint threadId)
        {
            return VTable.SetCurrentThreadSystemId(Self, threadId);
        }

        public HResult GetThreadTeb(uint threadId, out ulong teb)
        {
            // The native code may zero out this return pointer
            teb = 0;
            return VTable.GetThreadTeb(Self, threadId, out teb);
        }

        public HResult VirtualUnwind(uint threadId, uint contextSize, byte[] context)
        {
            fixed (byte* contextPtr = context)
            {
                return VTable.VirtualUnwind(Self, threadId, contextSize, contextPtr);
            }
        }

        public HResult GetSymbolPath(out string symbolPath)
        {
            symbolPath = null;

            // TODO: Get the path length first instead of using a fixed length buffer
            byte[] buffer = new byte[1024];
            fixed (byte* bufferPtr = buffer)
            {
                HResult hr = VTable.GetSymbolPath(Self, bufferPtr, (uint)buffer.Length, out uint pathSize);
                if (hr >= HResult.S_OK)
                {
                    if (pathSize > 0)
                    {
                        symbolPath = Encoding.ASCII.GetString(bufferPtr, (int)pathSize - 1);
                    }
                    else
                    {
                        hr = HResult.E_INVALIDARG;
                    }
                }
                return hr;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly unsafe struct IDebuggerServicesVTable
    {
        public readonly delegate* unmanaged[Stdcall]<IntPtr, out DebuggerServices.OperatingSystem, HResult> GetOperatingSystem;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, out DEBUG_CLASS, out DEBUG_CLASS_QUALIFIER, HResult> GetDebuggeeType;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, out IMAGE_FILE_MACHINE, HResult> GetExecutingProcessorType;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, byte*, byte*, IntPtr*, int, HResult> AddCommand;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, DEBUG_OUTPUT, byte*, void> OutputString;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ulong, byte*, uint, out int, HResult> ReadVirtual;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ulong, byte*, uint, out int, HResult> WriteVirtual;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, out uint, out uint, HResult> GetNumberModules;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, uint, ulong, byte*, uint, out uint, byte*, uint, uint*, byte*, uint, uint*, HResult> GetModuleNames;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, uint, out ulong, out ulong, out uint, out uint, HResult> GetModuleInfo;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, uint, ulong, byte*, byte*, uint, uint*, HResult> GetModuleVersionInformation; 
        public readonly delegate* unmanaged[Stdcall]<IntPtr, out uint, HResult> GetNumberThreads;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, uint, uint, uint*, uint*, HResult> GetThreadIdsByIndex;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, uint, uint, uint, byte*, HResult> GetThreadContextBySystemId;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, out uint, HResult> GetCurrentProcessSystemId;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, out uint, HResult> GetCurrentThreadSystemId;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, uint, HResult> SetCurrentThreadSystemId;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, uint, out ulong, HResult> GetThreadTeb;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, uint, uint, byte*, HResult> VirtualUnwind;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, byte*, uint, out uint, HResult> GetSymbolPath;
    }
}

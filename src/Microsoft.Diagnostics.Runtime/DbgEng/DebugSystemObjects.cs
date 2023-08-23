// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime.DbgEng
{
    internal sealed unsafe class DebugSystemObjects : CallableCOMWrapper
    {
        internal static readonly Guid IID_DebugSystemObjects3 = new("e9676e2f-e286-4ea3-b0f9-dfe5d9fc330e");

        public DebugSystemObjects(RefCountedFreeLibrary library, IntPtr pUnk)
            : base(library, IID_DebugSystemObjects3, pUnk)
        {
            SuppressRelease();
        }

        private ref readonly IDebugSystemObjects3VTable VTable => ref Unsafe.AsRef<IDebugSystemObjects3VTable>(_vtable);

#pragma warning disable CA1822
        public IDisposable Enter() => new SystemHolder();
#pragma warning restore CA1822

        public uint GetProcessId()
        {
            using IDisposable holder = Enter();
            HResult hr = VTable.GetCurrentProcessSystemId(Self, out uint id);
            return hr ? id : 0;
        }

        private HResult SetCurrentSystemId(int id)
        {
            HResult hr = VTable.SetCurrentSystemId(Self, id);
            DebugOnly.Assert(hr);
            return hr;
        }

        public HResult SetCurrentThread(uint id)
        {
            using IDisposable holder = Enter();
            HResult hr = VTable.SetCurrentThreadId(Self, id);
            DebugOnly.Assert(hr);
            return hr;
        }

        public uint GetCurrentThread()
        {
            using IDisposable holder = Enter();
            HResult hr = VTable.GetCurrentThreadId(Self, out uint id);

            return hr ? id : 0;
        }

        public int GetNumberThreads()
        {
            using IDisposable holder = Enter();
            HResult hr = VTable.GetNumberThreads(Self, out int count);
            DebugOnly.Assert(hr);
            return count;
        }

        public ulong GetThreadTeb(uint osThreadId)
        {
            using IDisposable holder = Enter();

            ulong teb = 0;
            uint currId = GetCurrentThread();

            uint debuggerThreadId = GetThreadIdBySystemId(osThreadId);
            HResult hr = SetCurrentThread(debuggerThreadId);
            if (hr)
            {
                hr = VTable.GetCurrentThreadTeb(Self, out teb);
                if (!hr)
                    teb = 0;
            }

            SetCurrentThread(currId);
            return teb;
        }

        internal void Init()
        {
            HResult hr = VTable.GetCurrentSystemId(Self, out int id);
            DebugOnly.Assert(hr);
            _systemId = id;
        }

        public uint[] GetThreadIds()
        {
            using IDisposable holder = Enter();

            int count = GetNumberThreads();
            if (count == 0)
                return Array.Empty<uint>();

            uint[] result = new uint[count];
            fixed (uint* pResult = result)
            {
                HResult hr = VTable.GetThreadIdsByIndex(Self, 0, count, null, pResult);
                if (hr)
                    return result;

                return Array.Empty<uint>();
            }
        }

        public uint GetThreadIdBySystemId(uint sysId)
        {
            using IDisposable holder = Enter();
            HResult hr = VTable.GetThreadIdBySystemId(Self, sysId, out uint result);
            DebugOnly.Assert(hr);
            return result;
        }

        private int _systemId = -1;

        private sealed class SystemHolder : IDisposable
        {
            private static readonly object _sync = new();

            public SystemHolder()
            {
                Monitor.Enter(_sync);
            }

            public void Dispose()
            {
                Monitor.Exit(_sync);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly unsafe struct IDebugSystemObjects3VTable
    {
        public readonly IntPtr GetEventThread;
        public readonly IntPtr GetEventProcess;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, out uint, int> GetCurrentThreadId;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, uint, int> SetCurrentThreadId;
        public readonly IntPtr GetCurrentProcessId;
        public readonly IntPtr SetCurrentProcessId;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, out int, int> GetNumberThreads;
        public readonly IntPtr GetTotalNumberThreads;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, int, int, int*, uint*, int> GetThreadIdsByIndex;
        public readonly IntPtr GetThreadIdByProcessor;
        public readonly IntPtr GetCurrentThreadDataOffset;
        public readonly IntPtr GetThreadIdByDataOffset;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, out ulong, int> GetCurrentThreadTeb;
        public readonly IntPtr GetThreadIdByTeb;
        public readonly IntPtr GetCurrentThreadSystemId;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, uint, out uint, int> GetThreadIdBySystemId;
        public readonly IntPtr GetCurrentThreadHandle;
        public readonly IntPtr GetThreadIdByHandle;
        public readonly IntPtr GetNumberProcesses;
        public readonly IntPtr GetProcessIdsByIndex;
        public readonly IntPtr GetCurrentProcessDataOffset;
        public readonly IntPtr GetProcessIdByDataOffset;
        public readonly IntPtr GetCurrentProcessPeb;
        public readonly IntPtr GetProcessIdByPeb;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, out uint, int> GetCurrentProcessSystemId;
        public readonly IntPtr GetProcessIdBySystemId;
        public readonly IntPtr GetCurrentProcessHandle;
        public readonly IntPtr GetProcessIdByHandle;
        public readonly IntPtr GetCurrentProcessExecutableName;
        public readonly IntPtr GetCurrentProcessUpTime;
        public readonly IntPtr GetImplicitThreadDataOffset;
        public readonly IntPtr SetImplicitThreadDataOffset;
        public readonly IntPtr GetImplicitProcessDataOffset;
        public readonly IntPtr SetImplicitProcessDataOffset;
        public readonly IntPtr GetEventSystem;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, out int, int> GetCurrentSystemId;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, int, int> SetCurrentSystemId;
        public readonly IntPtr GetNumberSystems;
        public readonly IntPtr GetSystemIdsByIndex;
        public readonly IntPtr GetTotalNumberThreadsAndProcesses;
        public readonly IntPtr GetCurrentSystemServer;
        public readonly IntPtr GetSystemByServer;
        public readonly IntPtr GetCurrentSystemServerName;
    }
}
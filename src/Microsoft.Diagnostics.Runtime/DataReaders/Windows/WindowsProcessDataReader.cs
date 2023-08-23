// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime.DataReaders.Windows;
using Microsoft.Diagnostics.Runtime.Implementation;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime
{
    internal sealed unsafe class WindowsProcessDataReader : CommonMemoryReader, IDataReader, IDisposable
    {
        private bool _disposed;
        private readonly WindowsThreadSuspender? _suspension;
        private readonly int _originalPid;
        private readonly IntPtr _snapshotHandle;
        private readonly IntPtr _cloneHandle;
        private readonly IntPtr _process;

        private const int PROCESS_VM_READ = 0x10;
        private const int PROCESS_QUERY_INFORMATION = 0x0400;

        public string DisplayName => $"pid:{ProcessId:x}";
        public OSPlatform TargetPlatform => OSPlatform.Windows;

        public WindowsProcessDataReader(int processId, WindowsProcessDataReaderMode mode)
        {
            if (mode == WindowsProcessDataReaderMode.Snapshot)
            {
                _originalPid = processId;

                // Throws InvalidOperationException, which is similar to how Process.Start fails if it can't start the process.
                Process process = Process.GetProcessById(processId);
                int hr = PssCaptureSnapshot(process.Handle, PSS_CAPTURE_FLAGS.PSS_CAPTURE_VA_CLONE, IntPtr.Size == 8 ? 0x0010001F : 0x0001003F, out _snapshotHandle);
                if (hr != 0)
                    throw new InvalidOperationException($"Could not create snapshot to process. Error {hr}.");

                hr = PssQuerySnapshot(_snapshotHandle, PSS_QUERY_INFORMATION_CLASS.PSS_QUERY_VA_CLONE_INFORMATION, out _cloneHandle, IntPtr.Size);
                if (hr != 0)
                    throw new InvalidOperationException($"Could not create snapshot to process. Error {hr}.");

                ProcessId = GetProcessId(_cloneHandle);
            }
            else
            {
                ProcessId = processId;
            }

            _process = WindowsFunctions.NativeMethods.OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false, ProcessId);

            if (_process == IntPtr.Zero)
            {
                if (!WindowsFunctions.IsProcessRunning(ProcessId))
                    throw new ArgumentException($"Process {processId} is not running.");

                int hr = Marshal.GetLastWin32Error();
                throw new ArgumentException($"Could not attach to process {processId}, error: {hr:x}");
            }

            using Process p = Process.GetCurrentProcess();
            if (DataTarget.PlatformFunctions.TryGetWow64(p.Handle, out bool wow64)
                && DataTarget.PlatformFunctions.TryGetWow64(_process, out bool targetWow64)
                && wow64 != targetWow64)
            {
                throw new InvalidOperationException("Mismatched architecture between this process and the target process.");
            }

            if (mode == WindowsProcessDataReaderMode.Suspend)
                _suspension = new WindowsThreadSuspender(ProcessId);
        }

        private void Dispose(bool _)
        {
            if (!_disposed)
            {
                _suspension?.Dispose();

                if (_originalPid != 0)
                {
                    // We don't want to throw an exception when we fail to free a snapshot.  In practice we never expect this to fail.
                    // If we were able to create a snapshot we should be able to free it.  Throwing an exception here means that our
                    // DataTarget.Dispose call (normally at the end of a using statement) will throw, and that is really annoying
                    // to code around.  Instead we'll log a message to any Trace listener, but otherwise continue on.
                    int hr = PssFreeSnapshot(Process.GetCurrentProcess().Handle, _snapshotHandle);
                    DebugOnly.Assert(hr == 0);

                    if (hr != 0)
                        Trace.WriteLine($"Unable to free the snapshot of the process we took: hr={hr}");
                }

                if (_process != IntPtr.Zero)
                    WindowsFunctions.NativeMethods.CloseHandle(_process);

                if (_originalPid != 0)
                {
                    try
                    {
                        // Kill the cloned process as we don't need it anymore
                        Process.GetProcessById(ProcessId).Kill();
                    }
                    catch (Exception e)
                    {
                        Trace.WriteLine($"Unable to kill the target process: {e}");
                    }
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~WindowsProcessDataReader()
        {
            Dispose(false);
        }

        public int ProcessId { get; }

        public bool IsThreadSafe => true;

        public void FlushCachedData()
        {
        }

        public Architecture Architecture => IntPtr.Size == 4 ? Architecture.X86 : Architecture.X64;

        public IEnumerable<ModuleInfo> EnumerateModules()
        {
            EnumProcessModules(_process, null, 0, out uint needed);

            IntPtr[] modules = new IntPtr[needed / IntPtr.Size];

            if (!EnumProcessModules(_process, modules, needed, out _))
                throw new InvalidOperationException("Unable to get process modules.");

            List<ModuleInfo> result = new(modules.Length);

            for (int i = 0; i < modules.Length; i++)
            {
                IntPtr ptr = modules[i];

                const int BufferSize = 1024;
                char[] buffer = ArrayPool<char>.Shared.Rent(BufferSize);
                string fileName;
                try
                {
                    uint res = GetModuleFileNameEx(_process, ptr, buffer, BufferSize);
                    DebugOnly.Assert(res != 0);

                    int len = Array.IndexOf(buffer, (char)0);
                    if (len >= 0)
                        fileName = new(buffer, 0, len);
                    else
                        fileName = new(buffer);
                }
                finally
                {
                    ArrayPool<char>.Shared.Return(buffer);
                }


                ulong baseAddr = (ulong)ptr.ToInt64();
                GetFileProperties(baseAddr, out int filesize, out int timestamp);

                Version? version = null;
                if (DataTarget.PlatformFunctions.GetFileVersion(fileName, out int major, out int minor, out int revision, out int patch))
                    version = new Version(major, minor, revision, patch);

                ModuleInfo module = new PEModuleInfo(this, baseAddr, fileName, true, timestamp, filesize, version);
                result.Add(module);
            }

            return result;
        }

        public override int Read(ulong address, Span<byte> buffer)
        {
            DebugOnly.Assert(!buffer.IsEmpty);
            try
            {
                fixed (byte* ptr = buffer)
                {
                    int res = ReadProcessMemory(_process, address.AsIntPtr(), ptr, new IntPtr(buffer.Length), out IntPtr read);
                    return (int)read;
                }
            }
            catch (OverflowException)
            {
                return 0;
            }
        }

        public bool GetThreadContext(uint threadID, uint contextFlags, Span<byte> context)
        {
            // We need to set the ContextFlags field to be the value of contextFlags.  For AMD64, that field is
            // at offset 0x30. For all other platforms that field is at offset 0.  We test here whether the context
            // is large enough to write the flags and then assign the value based on the architecture's offset.

            bool amd64 = Architecture == Architecture.X64;
            if (context.Length < 4 || (amd64 && context.Length < 0x34))
                return false;

            if (amd64)
            {
                fixed (byte* ptr = context)
                {
                    AMD64Context* ctx = (AMD64Context*)ptr;
                    ctx->ContextFlags = contextFlags;
                }
            }
            else
            {
                fixed (byte* ptr = context)
                {
                    uint* intPtr = (uint*)ptr;
                    *intPtr = contextFlags;
                }
            }

            using SafeWin32Handle thread = OpenThread(ThreadAccess.THREAD_ALL_ACCESS, true, threadID);
            if (thread.IsInvalid)
                return false;

            fixed (byte* ptr = context)
                return GetThreadContext(thread.DangerousGetHandle(), new IntPtr(ptr));
        }

        private void GetFileProperties(ulong moduleBase, out int filesize, out int timestamp)
        {
            filesize = 0;
            timestamp = 0;

            Span<byte> buffer = stackalloc byte[sizeof(uint)];

            if (Read(moduleBase + 0x3c, buffer) == buffer.Length)
            {
                uint sigOffset = buffer.AsUInt32();
                int sigLength = 4;

                if (Read(moduleBase + sigOffset, buffer) == buffer.Length)
                {
                    uint header = buffer.AsUInt32();

                    // Ensure the module contains the magic "PE" value at the offset it says it does.  This check should
                    // never fail unless we have the wrong base address for CLR.
                    DebugOnly.Assert(header == 0x4550);
                    if (header == 0x4550)
                    {
                        const int timeDataOffset = 4;
                        const int imageSizeOffset = 0x4c;
                        if (Read(moduleBase + sigOffset + (ulong)sigLength + timeDataOffset, buffer) == buffer.Length)
                            timestamp = buffer.AsInt32();

                        if (Read(moduleBase + sigOffset + (ulong)sigLength + imageSizeOffset, buffer) == buffer.Length)
                            filesize = buffer.AsInt32();
                    }
                }
            }
        }

        private const string Kernel32LibraryName = "kernel32.dll";

        [DllImport(Kernel32LibraryName, SetLastError = true, EntryPoint = "K32EnumProcessModules")]
        public static extern bool EnumProcessModules(IntPtr hProcess, [Out] IntPtr[]? lphModule, uint cb, [MarshalAs(UnmanagedType.U4)] out uint lpcbNeeded);

        [DllImport(Kernel32LibraryName, CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "K32GetModuleFileNameExW")]
        public static extern uint GetModuleFileNameEx(IntPtr hProcess, IntPtr hModule, [Out] char[] lpFilename, [MarshalAs(UnmanagedType.U4)] int nSize);

        [DllImport(Kernel32LibraryName)]
        private static extern int ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            byte* lpBuffer,
            IntPtr dwSize,
            out IntPtr lpNumberOfBytesRead);

        [DllImport(Kernel32LibraryName, SetLastError = true)]
        internal static extern int VirtualQueryEx(IntPtr hProcess, IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, IntPtr dwLength);

        [DllImport(Kernel32LibraryName)]
        private static extern bool GetThreadContext(IntPtr hThread, IntPtr lpContext);

        [DllImport(Kernel32LibraryName, SetLastError = true)]
        internal static extern SafeWin32Handle OpenThread(ThreadAccess dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwThreadId);

        [DllImport(Kernel32LibraryName, SetLastError = true)]
        internal static extern int SuspendThread(IntPtr hThread);

        [DllImport(Kernel32LibraryName, SetLastError = true)]
        internal static extern int ResumeThread(IntPtr hThread);

        [DllImport(Kernel32LibraryName)]
        private static extern int PssCaptureSnapshot(IntPtr ProcessHandle, PSS_CAPTURE_FLAGS CaptureFlags, int ThreadContextFlags, out IntPtr SnapshotHandle);

        [DllImport(Kernel32LibraryName)]
        private static extern int PssFreeSnapshot(IntPtr ProcessHandle, IntPtr SnapshotHandle);

        [DllImport(Kernel32LibraryName)]
        private static extern int PssQuerySnapshot(IntPtr SnapshotHandle, PSS_QUERY_INFORMATION_CLASS InformationClass, out IntPtr Buffer, int BufferLength);

        [DllImport(Kernel32LibraryName)]
        private static extern int GetProcessId(IntPtr hObject);
    }
}
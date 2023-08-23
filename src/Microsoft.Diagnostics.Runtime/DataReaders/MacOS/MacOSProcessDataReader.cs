// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Diagnostics.Runtime.DataReaders.Implementation;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime.MacOS
{
    internal sealed class MacOSProcessDataReader : CommonMemoryReader, IDataReader, IDisposable, IThreadReader
    {
        private readonly int _task;

        private ImmutableArray<MemoryRegion>.Builder _memoryRegions;
        private readonly Dictionary<ulong, uint> _threadActs = new(); // map of thread id (uint64_t) -> thread (thread_act_t)

        private bool _suspended;
        private bool _disposed;
        private readonly int _machTaskSelf;

        public MacOSProcessDataReader(int processId, bool suspend)
        {
            int status = Native.kill(processId, 0);
            if (status < 0 && Marshal.GetLastWin32Error() != Native.EPERM)
                throw new ArgumentException("The process is not running");

            ProcessId = processId;

            _machTaskSelf = Native.mach_task_self();

            int kr = Native.task_for_pid(_machTaskSelf, processId, out int task);
            if (kr != 0)
                throw new ClrDiagnosticsException($"task_for_pid failed with status code 0x{kr:x}");

            _task = task;

            if (suspend)
            {
                status = Native.ptrace(Native.PT_ATTACH, processId);

                if (status >= 0)
                    status = Native.waitpid(processId, IntPtr.Zero, 0);

                if (status < 0)
                {
                    int errno = Marshal.GetLastWin32Error();
                    throw new ClrDiagnosticsException($"Could not attach to process {processId}, errno: {errno}", errno);
                }

                _suspended = true;
            }

            _memoryRegions = LoadMemoryRegions();
            Architecture = RuntimeInformation.ProcessArchitecture;
        }

        ~MacOSProcessDataReader() => Dispose(false);

        public string DisplayName => $"pid:{ProcessId:x}";

        public bool IsThreadSafe => false;

        public OSPlatform TargetPlatform => OSPlatform.OSX;

        public Architecture Architecture { get; }

        public int ProcessId { get; }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            foreach (uint threadAct in _threadActs.Values)
                _ = Native.mach_port_deallocate(_machTaskSelf, threadAct);

            if (_suspended)
            {
                int status = Native.ptrace(Native.PT_DETACH, ProcessId);
                if (status < 0)
                {
                    int errno = Marshal.GetLastWin32Error();

                    // We don't want to bring down the process from the finalizer thread.
                    // We'll only throw here if we are in a dispose call.
                    if (disposing)
                        throw new ClrDiagnosticsException($"Could not detach from process {ProcessId}, errno: {errno}");
                    else
                        Trace.WriteLine($"Could not detach from process {ProcessId}, errno: {errno}");
                }

                _suspended = false;
            }

            _ = Native.mach_port_deallocate(_machTaskSelf, (uint)_task);

            _disposed = true;
        }

        public void FlushCachedData()
        {
            _memoryRegions = LoadMemoryRegions();
        }

        public IEnumerable<ModuleInfo> EnumerateModules()
        {
            int taskInfoCount = Native.TASK_DYLD_INFO_COUNT;
            int kr = Native.task_info(_task, Native.TASK_DYLD_INFO, out Native.task_dyld_info dyldInfo, ref taskInfoCount);
            if (kr != 0)
                throw new ClrDiagnosticsException();

            Native.dyld_all_image_infos infos = Read<Native.dyld_all_image_infos>(dyldInfo.all_image_info_addr);
            for (uint i = 0; i < infos.infoArrayCount; i++)
            {
                // TODO:  UUID?

                Native.dyld_image_info info = Read<Native.dyld_image_info>(infos.infoArray, i);
                ulong imageAddress = info.imageLoadAddress;
                string imageFilePath = ReadNullTerminatedAscii(info.imageFilePath);

                MachOModule module = new(this, imageAddress, imageFilePath);
                Version version = GetVersionInfo(info.imageLoadAddress) ?? new Version();
                yield return new MachOModuleInfo(module, imageAddress, imageFilePath, version, 0);
            }

            unsafe T Read<T>(ulong address, uint index = 0)
                where T : unmanaged
            {
                T result;
                if (Native.vm_read_overwrite(_task, address + index * (uint)sizeof(T), sizeof(T), &result, out _) != 0)
                    return default;

                return result;
            }

            unsafe string ReadNullTerminatedAscii(ulong address)
            {
                StringBuilder builder = new(64);
                byte* bytes = stackalloc byte[64];

                bool done = false;
                while (!done && (Native.vm_read_overwrite(_task, address, 64, bytes, out long read) == 0))
                {
                    address += (ulong)read;
                    for (int i = 0; !done && i < read; i++)
                    {
                        if (bytes[i] != 0)
                            builder.Append((char)bytes[i]);
                        else
                            done = true;
                    }
                }

                return builder.ToString();
            }
        }

        public unsafe Version? GetVersionInfo(ulong baseAddress)
        {
            if (!Read(baseAddress, out MachOHeader64 header) || header.Magic != MachOHeader64.ExpectedMagic)
                return null;

            baseAddress += (uint)sizeof(MachOHeader64);

            byte[] dataSegmentName = Encoding.ASCII.GetBytes("__DATA\0");
            byte[] dataSectionName = Encoding.ASCII.GetBytes("__data\0");
            for (int c = 0; c < header.NumberOfCommands; c++)
            {
                MachOCommand command = Read<MachOCommand>(ref baseAddress);
                MachOCommandType commandType = command.Command;
                int commandSize = command.CommandSize;

                if (commandType == MachOCommandType.Segment64)
                {
                    ulong prevAddress = baseAddress;
                    MachOSegmentCommand64 segmentCommand = Read<MachOSegmentCommand64>(ref baseAddress);
                    if (new ReadOnlySpan<byte>(segmentCommand.SegmentName, dataSegmentName.Length).SequenceEqual(dataSegmentName))
                    {
                        for (int s = 0; s < segmentCommand.NumberOfSections; s++)
                        {
                            MachOSection64 section = Read<MachOSection64>(ref baseAddress);
                            if (new ReadOnlySpan<byte>(section.SectionName, dataSectionName.Length).SequenceEqual(dataSectionName))
                            {
                                long dataOffset = section.Address;
                                long dataSize = section.Size;
                                if (this.GetVersionInfo(baseAddress + (ulong)dataOffset, (ulong)dataSize, out Version? version))
                                    return version;
                            }
                        }

                        break;
                    }

                    baseAddress = prevAddress;
                }

                baseAddress += (uint)(commandSize - sizeof(MachOCommand));
            }

            return null;
        }

        public override unsafe int Read(ulong address, Span<byte> buffer)
        {
            DebugOnly.Assert(!buffer.IsEmpty);

            int readable = this.GetReadableBytesCount(_memoryRegions, address, buffer.Length);
            if (readable <= 0)
            {
                return 0;
            }

            fixed (byte* ptr = buffer)
            {
                int kr = Native.vm_read_overwrite(_task, address, readable, ptr, out long read);
                if (kr != 0)
                    return 0;

                return (int)read;
            }
        }

        private unsafe T Read<T>(ref ulong address)
            where T : unmanaged
        {
            T result = Read<T>(address);
            address += (uint)sizeof(T);
            return result;
        }

        public IEnumerable<uint> EnumerateOSThreadIds()
        {
            LoadThreads();
            return _threadActs.Keys.Select(threadID => checked((uint)threadID));
        }

        public ulong GetThreadTeb(uint _) => 0;

        public unsafe bool GetThreadContext(uint threadID, uint contextFlags, Span<byte> context)
        {
            LoadThreads();
            if (!_threadActs.TryGetValue(threadID, out uint threadAct))
                return false;

            int stateFlavor;
            int stateCount;
            int regSize;
            (stateFlavor, stateCount, regSize) = Architecture switch
            {
                Architecture.X64 => (Native.x86_THREAD_STATE64, Native.x86_THREAD_STATE64_COUNT, sizeof(x86_thread_state64_t)),
                Architecture.Arm64 => (Native.ARM_THREAD_STATE64, Native.ARM_THREAD_STATE64_COUNT, sizeof(arm_thread_state64_t)),
                _ => throw new PlatformNotSupportedException()
            };

            byte[] buffer = ArrayPool<byte>.Shared.Rent(regSize);
            try
            {
                fixed (byte* data = buffer)
                {
                    int kr = Native.thread_get_state(threadAct, stateFlavor, new IntPtr(data), ref stateCount);
                    if (kr != 0)
                        return false;
                }

                switch (Architecture)
                {
                    case Architecture.X64:
                        Unsafe.As<byte, x86_thread_state64_t>(ref MemoryMarshal.GetReference(buffer.AsSpan())).CopyContext(context);
                        break;
                    case Architecture.Arm64:
                        Unsafe.As<byte, arm_thread_state64_t>(ref MemoryMarshal.GetReference(buffer.AsSpan())).CopyContext(context);
                        break;
                    default:
                        throw new PlatformNotSupportedException();
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            return true;
        }

        private unsafe void LoadThreads()
        {
            if (_threadActs.Count == 0)
            {
                uint* threads = null;
                int kr = Native.task_threads(_task, &threads, out uint threadsCount);
                if (kr != 0)
                    throw new ClrDiagnosticsException($"task_threads failed with status code 0x{kr:x}");

                try
                {
                    for (uint i = 0; i < threadsCount; ++i)
                    {
                        int threadInfoCount = Native.THREAD_IDENTIFIER_INFO_COUNT;
                        kr = Native.thread_info(threads[i], Native.THREAD_IDENTIFIER_INFO, out Native.thread_identifier_info identifierInfo, ref threadInfoCount);
                        if (kr != 0)
                            continue;

                        _threadActs.Add(identifierInfo.thread_id, threads[i]);
                    }
                }
                finally
                {
                    _ = Native.mach_vm_deallocate(Native.mach_task_self(), (ulong)threads, threadsCount * sizeof(uint));
                }
            }
        }

        private ImmutableArray<MemoryRegion>.Builder LoadMemoryRegions()
        {
            ImmutableArray<MemoryRegion>.Builder result = ImmutableArray.CreateBuilder<MemoryRegion>();

            ulong address = 0;
            int infoCount = Native.VM_REGION_BASIC_INFO_COUNT_64;
            while (true)
            {
                int kr = Native.mach_vm_region(_task, ref address, out ulong size, Native.VM_REGION_BASIC_INFO_64, out Native.vm_region_basic_info_64 info, ref infoCount, out _);
                if (kr != 0)
                    if (kr != Native.KERN_INVALID_ADDRESS)
                        throw new ClrDiagnosticsException();
                    else
                        break;

                ulong endAddress = address + size;
                result.Add(new MemoryRegion
                {
                    BeginAddress = address,
                    EndAddress = endAddress,
                    Permission = info.protection,
                });

                address = endAddress;
            }

            return result;
        }

        internal static class Native
        {
            internal const int EPERM = 1;

            internal const int KERN_INVALID_ADDRESS = 1;

            internal const int PROT_READ = 0x01;

            internal const int PT_ATTACH = 10; // TODO: deprecated
            internal const int PT_DETACH = 11;

            internal const int TASK_DYLD_INFO = 17;
            internal const int THREAD_IDENTIFIER_INFO = 4;
            internal const int x86_THREAD_STATE64 = 4;
            internal const int ARM_THREAD_STATE64 = 6;
            internal const int VM_REGION_BASIC_INFO_64 = 9;

            internal static readonly unsafe int TASK_DYLD_INFO_COUNT = sizeof(task_dyld_info) / sizeof(uint);
            internal static readonly unsafe int THREAD_IDENTIFIER_INFO_COUNT = sizeof(thread_identifier_info) / sizeof(uint);
            internal static readonly unsafe int x86_THREAD_STATE64_COUNT = sizeof(x86_thread_state64_t) / sizeof(uint);
            internal static readonly unsafe int ARM_THREAD_STATE64_COUNT = sizeof(arm_thread_state64_t) / sizeof(uint);
            internal static readonly unsafe int VM_REGION_BASIC_INFO_COUNT_64 = sizeof(vm_region_basic_info_64) / sizeof(int);

            private const string LibSystem = "libSystem.dylib";

            [DllImport(LibSystem, SetLastError = true)]
            internal static extern int kill(int pid, int sig);

            [DllImport(LibSystem)]
            internal static extern int mach_task_self();

            [DllImport(LibSystem, SetLastError = true)]
            internal static extern int ptrace(int request, int pid, IntPtr addr = default, int data = default);

            [DllImport(LibSystem)]
            internal static extern int task_for_pid(int parent, int pid, out int task);

            [DllImport(LibSystem)]
            internal static extern int task_info(int target_task, uint flavor, out /*int*/task_dyld_info task_info, ref /*uint*/int task_info_count);

            [DllImport(LibSystem)]
            internal static extern unsafe int task_threads(int target_task, uint** act_list, out uint act_list_count);

            [DllImport(LibSystem)]
            internal static extern int thread_info(uint target_act, uint flavor, out /*int*/thread_identifier_info thread_info, ref /*uint*/int thread_info_count);

            [DllImport(LibSystem)]
            internal static extern int thread_get_state(uint target_act, int flavor, /*uint**/IntPtr old_state, ref /*uint*/int old_state_count);

            [DllImport(LibSystem)]
            internal static extern unsafe int vm_read_overwrite(int target_task, /*UIntPtr*/ulong address, /*UIntPtr*/long size, /*UIntPtr*/void* data, out /*UIntPtr*/long data_size);

            [DllImport(LibSystem)]
            internal static extern int mach_vm_region(int target_task, ref /*UIntPtr*/ulong address, out /*UIntPtr*/ulong size, int flavor, out /*int*/vm_region_basic_info_64 info, ref /*uint*/int info_count, out int object_name);

            [DllImport(LibSystem)]
            internal static extern int mach_vm_deallocate(int target_task, /*UIntPtr*/ulong address, /*UIntPtr*/ulong size);

            [DllImport(LibSystem)]
            internal static extern int mach_port_deallocate(/*uint*/int task, uint name);

            [DllImport(LibSystem)]
            internal static extern int waitpid(int pid, IntPtr status, int options);

            internal readonly struct dyld_all_image_infos
            {
                internal readonly uint version;
                internal readonly uint infoArrayCount;
                internal readonly ulong infoArray;

                // We don't need the rest of this struct so we do not define the rest of the fields.
            }

            internal readonly struct dyld_image_info
            {
                internal readonly ulong imageLoadAddress;
                internal readonly ulong imageFilePath;
                internal readonly ulong imageFileModDate;
            }

            internal readonly struct task_dyld_info
            {
                internal readonly ulong all_image_info_addr;
                internal readonly ulong all_image_info_size;
                internal readonly int all_image_info_format;
            }

            internal readonly struct thread_identifier_info
            {
                internal readonly ulong thread_id;
                internal readonly ulong thread_handle;
                internal readonly ulong dispatch_qaddr;
            }

            internal readonly struct vm_region_basic_info_64
            {
                internal readonly int protection;
                internal readonly int max_protection;
                internal readonly uint inheritance;
                internal readonly uint shared;
                internal readonly uint reserved;
                internal readonly ulong offset;
                internal readonly int behavior;
                internal readonly ushort user_wired_count;
            }
        }

        internal struct MemoryRegion : IRegion
        {
            public ulong BeginAddress { get; set; }
            public ulong EndAddress { get; set; }

            public int Permission { get; set; }

            public bool IsReadable => (Permission & Native.PROT_READ) != 0;
        }
    }
}

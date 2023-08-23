// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime.DataReaders.Implementation;
using Microsoft.Diagnostics.Runtime.Implementation;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    /// <summary>
    /// A data reader that targets a Linux process.
    /// The current process must have ptrace access to the target process.
    /// </summary>
    internal sealed class LinuxLiveDataReader : CommonMemoryReader, IDataReader, IDisposable, IThreadReader
    {
        private ImmutableArray<MemoryMapEntry>.Builder _memoryMapEntries;
        private readonly List<uint> _threadIDs = new();

        private bool _suspended;
        private bool _disposed;

        public string DisplayName => $"pid:{ProcessId:x}";
        public OSPlatform TargetPlatform => OSPlatform.Linux;

        public LinuxLiveDataReader(int processId, bool suspend)
        {
            int status = kill(processId, 0);
            if (status < 0 && Marshal.GetLastWin32Error() != EPERM)
                throw new ArgumentException("The process is not running");

            ProcessId = processId;
            _memoryMapEntries = LoadMemoryMaps();

            if (suspend)
            {
                LoadThreadsAndAttach();
                _suspended = true;
            }

            Architecture = RuntimeInformation.ProcessArchitecture;
        }

        ~LinuxLiveDataReader() => Dispose(false);

        public int ProcessId { get; }

        public bool IsThreadSafe => false;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool _)
        {
            if (_disposed)
                return;

            if (_suspended)
            {
                foreach (uint tid in _threadIDs)
                {
                    // no point in handling errors here as the user can do nothing with them
                    // also if Dispose is called from the finalizer we could crash the process
                    ptrace(PTRACE_DETACH, (int)tid, IntPtr.Zero, IntPtr.Zero);
                }
                _suspended = false;
            }

            _disposed = true;
        }

        public void FlushCachedData()
        {
            _threadIDs.Clear();
            _memoryMapEntries = LoadMemoryMaps();
        }

        public Architecture Architecture { get; }

        public IEnumerable<ModuleInfo> EnumerateModules() =>
            from entry in _memoryMapEntries
            where !string.IsNullOrEmpty(entry.FilePath)
            group entry by entry.FilePath into image
            let filePath = image.Key
            let containsExecutable = image.Any(entry => entry.IsExecutable)
            let beginAddress = image.Min(entry => entry.BeginAddress)
            select GetModuleInfo(this, beginAddress, filePath, containsExecutable);

        private ModuleInfo GetModuleInfo(IDataReader reader, ulong baseAddress, string filePath, bool isVirtual)
        {
            if (reader.Read<ushort>(baseAddress) == 0x5a4d)
                return new PEModuleInfo(reader, baseAddress, filePath, isVirtual);

            long size = 0;
            FileInfo fileInfo = new(filePath);
            if (fileInfo.Exists)
                size = fileInfo.Length;

            return new ElfModuleInfo(reader, GetElfFile(baseAddress), baseAddress, size, filePath);
        }

        private ElfFile? GetElfFile(ulong baseAddress)
        {
            try
            {
                return new ElfFile(this, baseAddress);
            }
            catch (InvalidDataException)
            {
                return null;
            }
        }

        public override int Read(ulong address, Span<byte> buffer)
        {
            DebugOnly.Assert(!buffer.IsEmpty);
            return ReadMemoryReadv(address, buffer);
        }

        private unsafe int ReadMemoryReadv(ulong address, Span<byte> buffer)
        {
            int readableBytesCount = this.GetReadableBytesCount(this._memoryMapEntries, address, buffer.Length);
            if (readableBytesCount <= 0)
            {
                return 0;
            }

            fixed (byte* ptr = buffer)
            {
                IOVEC local = new()
                {
                    iov_base = ptr,
                    iov_len = (IntPtr)readableBytesCount
                };
                IOVEC remote = new()
                {
                    iov_base = (void*)address,
                    iov_len = (IntPtr)readableBytesCount
                };
                int read = (int)process_vm_readv(ProcessId, &local, (UIntPtr)1, &remote, (UIntPtr)1, UIntPtr.Zero).ToInt64();
                if (read < 0)
                {
                    return Marshal.GetLastWin32Error() switch
                    {
                        EPERM => throw new UnauthorizedAccessException(),
                        ESRCH => throw new InvalidOperationException("The process has exited"),
                        _ => 0
                    };
                }

                return read;
            }
        }

        public IEnumerable<uint> EnumerateOSThreadIds()
        {
            LoadThreads();
            return _threadIDs;
        }

        public ulong GetThreadTeb(uint _) => 0;

        public unsafe bool GetThreadContext(uint threadID, uint contextFlags, Span<byte> context)
        {
            LoadThreads();
            if (!_threadIDs.Contains(threadID) || Architecture == Architecture.X86)
                return false;

            int regSize = Architecture switch
            {
                Architecture.Arm => sizeof(RegSetArm),
                Architecture.Arm64 => sizeof(RegSetArm64),
                Architecture.X64 => sizeof(RegSetX64),
                _ => sizeof(RegSetX86),
            };

            byte[] buffer = ArrayPool<byte>.Shared.Rent(regSize);
            try
            {
                fixed (byte* data = buffer)
                {
                    ptrace(PTRACE_GETREGS, (int)threadID, IntPtr.Zero, new IntPtr(data));
                }

                switch (Architecture)
                {
                    case Architecture.Arm:
                        Unsafe.As<byte, RegSetArm>(ref MemoryMarshal.GetReference(buffer.AsSpan())).CopyContext(context);
                        break;
                    case Architecture.Arm64:
                        Unsafe.As<byte, RegSetArm64>(ref MemoryMarshal.GetReference(buffer.AsSpan())).CopyContext(context);
                        break;
                    case Architecture.X64:
                        Unsafe.As<byte, RegSetX64>(ref MemoryMarshal.GetReference(buffer.AsSpan())).CopyContext(context);
                        break;
                    default:
                        Unsafe.As<byte, RegSetX86>(ref MemoryMarshal.GetReference(buffer.AsSpan())).CopyContext(context);
                        break;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            return true;
        }

        private void LoadThreadsAndAttach()
        {
            const int maxPasses = 100;
            HashSet<uint> tracees = new();
            bool makesProgress = true;
            // Make up to maxPasses to be sure to attach to the threads that could have been created in the meantime
            for (int i = 0; makesProgress && i < maxPasses; i++)
            {
                makesProgress = false;
                // GetThreads could throw during enumeration. It means the process was killed so no cleanup is needed.
                IEnumerable<uint> threads = GetThreads(ProcessId);
                foreach (uint tid in threads)
                {
                    if (tracees.Contains(tid))
                    {
                        // We have already attached successfully to this thread
                        continue;
                    }

                    int status = (int)ptrace(PTRACE_ATTACH, (int)tid, IntPtr.Zero, IntPtr.Zero);
                    if (status >= 0)
                    {
                        status = waitpid((int)tid, IntPtr.Zero, 0);
                    }
                    if (status >= 0)
                    {
                        tracees.Add(tid);
                        makesProgress = true;
                    }

                    if (status < 0)
                    {
                        // We failed to attach. It could mean multiple things:
                        // 1. The tid exited: it's ok we won't see it at the next iteration.
                        // 2. We don't have permissions: attach to other threads will likely fail, too, and we won't make progress
                        // 3. Something is weird with this particular thread. We'll keep it as is and try to attach to everything else
                        continue;
                    }
                }
            }

            if (tracees.Count == 0)
            {
                throw new ClrDiagnosticsException($"Could not PTRACE_ATTACH to any thread of the process {ProcessId}. Either the process has exited or you don't have permission.");
            }

            _threadIDs.AddRange(tracees);
        }

        private void LoadThreads()
        {
            if (_threadIDs.Count == 0)
            {
                _threadIDs.AddRange(GetThreads(ProcessId));
            }
        }

        private static IEnumerable<uint> GetThreads(int pid)
        {
            string taskDirPath = $"/proc/{pid}/task";
            foreach (string taskDir in Directory.EnumerateDirectories(taskDirPath))
            {
                string dirName = Path.GetFileName(taskDir);
                if (uint.TryParse(dirName, out uint taskId))
                {
                    yield return taskId;
                }
            }
        }

        private ImmutableArray<MemoryMapEntry>.Builder LoadMemoryMaps()
        {
            ImmutableArray<MemoryMapEntry>.Builder result = ImmutableArray.CreateBuilder<MemoryMapEntry>();
            string mapsFilePath = $"/proc/{ProcessId}/maps";
            using StreamReader reader = new(mapsFilePath);
            while (true)
            {
                string? line = reader.ReadLine();
                if (string.IsNullOrEmpty(line))
                {
                    break;
                }

                string address, permission, path;
                string[] parts = line.Split(new char[] { ' ' }, 6, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 5)
                {
                    path = string.Empty;
                }
                else if (parts.Length == 6)
                {
                    path = parts[5].StartsWith("[", StringComparison.Ordinal) ? string.Empty : parts[5];
                }
                else
                {
                    DebugOnly.Fail("Unknown data format");
                    continue;
                }

                address = parts[0];
                permission = parts[1];
                string[] addressBeginEnd = address.Split('-');
                MemoryMapEntry entry = new()
                {
                    BeginAddress = Convert.ToUInt64(addressBeginEnd[0], 16),
                    EndAddress = Convert.ToUInt64(addressBeginEnd[1], 16),
                    FilePath = path,
                    Permission = ParsePermission(permission)
                };
                result.Add(entry);
            }

            return result;
        }

        private static int ParsePermission(string permission)
        {
            DebugOnly.Assert(permission.Length == 4);

            // r = read
            // w = write
            // x = execute
            // s = shared
            // p = private (copy on write)
            int r = permission[0] == 'r' ? 8 : 0;
            int w = permission[1] == 'w' ? 4 : 0;
            int x = permission[2] == 'x' ? 2 : 0;
            int p = permission[3] == 'p' ? 1 : 0;
            return r | w | x | p;
        }

        private const int EPERM = 1;
        private const int ESRCH = 3;

        private const string LibC = "libc";

        [DllImport(LibC, SetLastError = true)]
        private static extern int kill(int pid, int sig);

        [DllImport(LibC, SetLastError = true)]
        private static extern IntPtr ptrace(int request, int pid, IntPtr addr, IntPtr data);

        [DllImport(LibC, SetLastError = true)]
        private static extern unsafe IntPtr process_vm_readv(int pid, IOVEC* local_iov, UIntPtr liovcnt, IOVEC* remote_iov, UIntPtr riovcnt, UIntPtr flags);

        [DllImport(LibC)]
        private static extern int waitpid(int pid, IntPtr status, int options);

        private unsafe struct IOVEC
        {
            public void* iov_base;
            public IntPtr iov_len;
        }

        private const int PTRACE_GETREGS = 12;
        private const int PTRACE_ATTACH = 16;
        private const int PTRACE_DETACH = 17;
    }

    internal struct MemoryMapEntry : IRegion
    {
        public ulong BeginAddress { get; set; }
        public ulong EndAddress { get; set; }
        public string? FilePath { get; set; }
        public int Permission { get; set; }

        public bool IsReadable => (Permission & 8) != 0;

        public bool IsExecutable => (Permission & 2) != 0;
    }
}

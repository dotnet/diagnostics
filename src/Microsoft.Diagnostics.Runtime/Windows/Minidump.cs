// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Runtime.Windows
{
    internal sealed class Minidump : IDisposable
    {
        private const int MiniDumpWithFullMemory = 0x2;
        private const int MiniDumpWithPrivateReadWriteMemory = 0x200;
        private const int MiniDumpWithPrivateWriteCopyMemory = 0x10000;

        private readonly string _displayName;
        private readonly MinidumpDirectory[] _directories;
        private readonly Task<ThreadReadResult> _threadTask;
        private readonly MemoryMappedFile? _file;
        private ImmutableArray<MinidumpContextData> _contextsCached;

        public MinidumpMemoryReader MemoryReader { get; }

        public ImmutableArray<MinidumpContextData> ContextData
        {
            get
            {
                if (!_contextsCached.IsDefault)
                    return _contextsCached;

                _contextsCached = _threadTask.Result.ContextData;
                return _contextsCached;
            }
        }

        public ImmutableArray<uint> OrderedThreads => _threadTask.Result.Threads;

        public ImmutableDictionary<uint, ulong> Tebs => _threadTask.Result.Tebs;

        public ImmutableArray<MinidumpModule> Modules { get; }

        public MinidumpProcessorArchitecture Architecture { get; }
        public int ProcessId { get; } = -1;

        public int PointerSize => Architecture switch
        {
            MinidumpProcessorArchitecture.Arm64 or MinidumpProcessorArchitecture.Amd64 => 8,
            MinidumpProcessorArchitecture.Intel or MinidumpProcessorArchitecture.Arm => 4,
            _ => throw new NotImplementedException($"Not implemented for architecture {Architecture}."),
        };

        public bool IsMiniDump { get; }

        public Minidump(string displayName, Stream stream, CacheOptions cacheOptions, bool leaveOpen)
        {
            _displayName = displayName;

            // Load header
            MinidumpHeader header = Read<MinidumpHeader>(stream);
            if (!header.IsValid)
                throw new InvalidDataException($"File '{displayName}' is not a Minidump.");

            IsMiniDump = (header.Flags & (MiniDumpWithFullMemory | MiniDumpWithPrivateReadWriteMemory | MiniDumpWithPrivateWriteCopyMemory)) == 0;

            _directories = new MinidumpDirectory[header.NumberOfStreams];

            stream.Position = header.StreamDirectoryRva;
            if (!Read(stream, _directories))
                throw new InvalidDataException($"Unable to read directories from minidump '{displayName} offset 0x{header.StreamDirectoryRva:x}");

            (int systemInfoIndex, int moduleListIndex, int miscStream) = FindImportantStreams(displayName);

            // Architecture is the first entry in MINIDUMP_SYSTEM_INFO.  We need nothing else out of that struct,
            // so we only read the first entry.
            // https://docs.microsoft.com/en-us/windows/win32/api/minidumpapiset/ns-minidumpapiset-minidump_system_info
            Architecture = Read<MinidumpProcessorArchitecture>(stream, _directories[systemInfoIndex].Rva);

            // ProcessId is the 3rd DWORD in this stream.
            if (miscStream != -1)
                ProcessId = Read<int>(stream, _directories[miscStream].Rva + sizeof(uint) * 2);

            // Initialize modules.  DataTarget will need a module list immediately, so there's no reason to delay
            // filling in the module list.
            long rva = _directories[moduleListIndex].Rva;
            uint count = Read<uint>(stream, rva);

            rva += sizeof(uint);
            MinidumpModule[] modules = new MinidumpModule[count];

            if (Read(stream, rva, modules))
                Modules = modules.AsImmutableArray();
            else
                Modules = ImmutableArray<MinidumpModule>.Empty;

            // Read segments async.
            ImmutableArray<MinidumpSegment> segments = GetSegments(stream);

            MinidumpMemoryReader memoryReader;
            if (stream is FileStream fs) // we can optimize for FileStreams
            {
                int cacheSize = cacheOptions.MaxDumpCacheSize > int.MaxValue ? int.MaxValue : (int)cacheOptions.MaxDumpCacheSize;
                bool isTinyDump = stream.Length <= cacheSize;
                if (isTinyDump)
                {
                    _file = MemoryMappedFile.CreateFromFile(fs, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: false);
                    MemoryMappedViewStream mmStream = _file.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);
                    memoryReader = new UncachedMemoryReader(segments, mmStream, PointerSize, leaveOpen);
                }
                else if (cacheSize < CachedMemoryReader.MinimumCacheSize)
                {
                    // this will be very slow
                    memoryReader = new UncachedMemoryReader(segments, stream, PointerSize, leaveOpen);
                }
                else
                {
                    CacheTechnology technology = cacheOptions.UseOSMemoryFeatures ? CacheTechnology.AWE : CacheTechnology.ArrayPool;
                    memoryReader = new CachedMemoryReader(segments, displayName, fs, cacheSize, technology, PointerSize, leaveOpen);
                }
            }
            else
            {
                memoryReader = new UncachedMemoryReader(segments, stream, PointerSize, leaveOpen);
            }

            MemoryReader = memoryReader;

            _threadTask = ReadThreadData(stream);
        }

        public void Dispose()
        {
            if (MemoryReader is IDisposable disposable)
                disposable.Dispose();

            _file?.Dispose();
        }

        public IEnumerable<MinidumpModuleInfo> EnumerateModuleInfo() => Modules.Select(m => new MinidumpModuleInfo(MemoryReader, m));

        private (int systemInfo, int moduleList, int miscInfo) FindImportantStreams(string crashDump)
        {
            int systemInfo = -1;
            int moduleList = -1;
            int miscInfo = -1;

            for (int i = 0; i < _directories.Length; i++)
            {
                switch (_directories[i].StreamType)
                {
                    case MinidumpStreamType.ModuleListStream:
                        if (moduleList != -1)
                            throw new InvalidDataException($"Minidump '{crashDump}' had multiple module lists.");

                        moduleList = i;
                        break;

                    case MinidumpStreamType.SystemInfoStream:
                        if (systemInfo != -1)
                            throw new InvalidDataException($"Minidump '{crashDump}' had multiple system info streams.");

                        systemInfo = i;
                        break;

                    case MinidumpStreamType.MiscInfoStream:
                        miscInfo = i;
                        break;
                }
            }

            if (systemInfo == -1)
                throw new InvalidDataException($"Minidump '{crashDump}' did not contain a system info stream.");
            if (moduleList == -1)
                throw new InvalidDataException($"Minidump '{crashDump}' did not contain a module list stream.");

            return (systemInfo, moduleList, miscInfo);
        }

        #region ReadThreadData
        private async Task<ThreadReadResult> ReadThreadData(Stream stream)
        {
            Dictionary<uint, (uint Rva, uint Size, ulong Teb)> threadContextLocations = new();

            // This will select ThreadListStread, ThreadExListStream, and ThreadInfoListStream in that order.
            // We prefer to pull contexts from the *ListStreams but if those don't exist or are missing threads
            // we still want threadIDs which don't have context records for IDataReader.EnumerateThreads.
            IOrderedEnumerable<MinidumpDirectory> directories = from d in _directories
                                                                where d.StreamType is MinidumpStreamType.ThreadListStream or
                                                                      MinidumpStreamType.ThreadExListStream or
                                                                      MinidumpStreamType.ThreadInfoListStream
                                                                orderby d.StreamType ascending
                                                                select d;

            ImmutableArray<uint>.Builder threadBuilder = ImmutableArray.CreateBuilder<uint>();

            byte[] buffer = ArrayPool<byte>.Shared.Rent(1024);
            try
            {
                foreach (MinidumpDirectory directory in _directories.Where(d => d.StreamType is MinidumpStreamType.ThreadListStream or MinidumpStreamType.ThreadExListStream))
                {
                    if (directory.StreamType == MinidumpStreamType.ThreadListStream)
                    {
                        uint numThreads = await ReadAsync<uint>(stream, buffer, directory.Rva).ConfigureAwait(false);
                        if (numThreads == 0)
                            continue;

                        int count = ResizeBytesForArray<MinidumpThread>(numThreads, ref buffer);
                        int read = await ReadAsync(stream, buffer, count).ConfigureAwait(false);

                        for (int i = 0; i < read; i += SizeOf<MinidumpThread>())
                        {
                            MinidumpThread thread = Unsafe.As<byte, MinidumpThread>(ref buffer[i]);

                            if (!threadContextLocations.ContainsKey(thread.ThreadId))
                                threadBuilder.Add(thread.ThreadId);

                            threadContextLocations[thread.ThreadId] = (thread.ThreadContext.Rva, thread.ThreadContext.DataSize, thread.Teb);
                        }
                    }
                    else if (directory.StreamType == MinidumpStreamType.ThreadExListStream)
                    {
                        uint numThreads = await ReadAsync<uint>(stream, buffer, directory.Rva).ConfigureAwait(false);
                        if (numThreads == 0)
                            continue;

                        int count = ResizeBytesForArray<MinidumpThreadEx>(numThreads, ref buffer);
                        int read = await ReadAsync(stream, buffer, count).ConfigureAwait(false);

                        for (int i = 0; i < read; i += SizeOf<MinidumpThreadEx>())
                        {
                            MinidumpThreadEx thread = Unsafe.As<byte, MinidumpThreadEx>(ref buffer[i]);

                            if (!threadContextLocations.ContainsKey(thread.ThreadId))
                                threadBuilder.Add(thread.ThreadId);

                            threadContextLocations[thread.ThreadId] = (thread.ThreadContext.Rva, thread.ThreadContext.DataSize, thread.Teb);
                        }
                    }
                    else if (directory.StreamType == MinidumpStreamType.ThreadInfoListStream)
                    {
                        MinidumpThreadInfoList threadInfoList = await ReadAsync<MinidumpThreadInfoList>(stream, buffer, directory.Rva).ConfigureAwait(false);
                        if (threadInfoList.NumberOfEntries <= 0)
                            continue;

                        if (threadInfoList.SizeOfEntry != SizeOf<MinidumpThreadInfo>())
                            throw new InvalidDataException($"ThreadInfoList.SizeOfEntry=0x{threadInfoList.SizeOfEntry:x}, but sizeof(MinidumpThreadInfo)=0x{SizeOf<MinidumpThreadInfo>()}");

                        stream.Position = directory.Rva + threadInfoList.SizeOfHeader;
                        int count = ResizeBytesForArray<MinidumpThreadInfo>((ulong)threadInfoList.NumberOfEntries, ref buffer);
                        int read = await ReadAsync(stream, buffer, count).ConfigureAwait(false);

                        for (int i = 0; i < read; i += threadInfoList.SizeOfEntry)
                        {
                            MinidumpThreadInfo thread = Unsafe.As<byte, MinidumpThreadInfo>(ref buffer[i]);
                            if (!threadContextLocations.ContainsKey(thread.ThreadId))
                            {
                                threadContextLocations[thread.ThreadId] = (0, 0, 0);
                                threadBuilder.Add(thread.ThreadId);
                            }
                        }
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            ImmutableArray<MinidumpContextData>.Builder contextBuilder = ImmutableArray.CreateBuilder<MinidumpContextData>(threadContextLocations.Count);
            ImmutableDictionary<uint, ulong>.Builder tebBuilder = ImmutableDictionary.CreateBuilder<uint, ulong>();

            foreach (KeyValuePair<uint, (uint Rva, uint Size, ulong Teb)> item in threadContextLocations.OrderBy(k => k.Key))
            {
                uint threadId = item.Key;

                contextBuilder.Add(new MinidumpContextData(threadId, item.Value.Rva, item.Value.Size));
                tebBuilder.Add(threadId, item.Value.Teb);
            }

            return new ThreadReadResult()
            {
                ContextData = contextBuilder.MoveOrCopyToImmutable(),
                Tebs = tebBuilder.ToImmutable(),
                Threads = threadBuilder.ToImmutable()
            };
        }

        private static async Task<int> ReadAsync(Stream stream, byte[] buffer, int count)
        {
#if NETCOREAPP3_1 || NET5_0
            return await stream.ReadAsync(buffer.AsMemory(0, count)).ConfigureAwait(false);
#else
            return await stream.ReadAsync(buffer, 0, count).ConfigureAwait(false);
#endif
        }
        #endregion

        private ImmutableArray<MinidumpSegment> GetSegments(Stream stream)
        {
            List<MinidumpSegment> segments = new();

            byte[]? buffer = ArrayPool<byte>.Shared.Rent(128);
            try
            {
                for (int i = 0; i < _directories.Length; i++)
                {
                    if (_directories[i].StreamType == MinidumpStreamType.MemoryListStream)
                    {
                        // MINIDUMP_MEMORY_LIST only contains a count followed by MINIDUMP_MEMORY_DESCRIPTORs
                        uint count = Read<uint>(stream, _directories[i].Rva);
                        int byteCount = ResizeBytesForArray<MinidumpMemoryDescriptor>(count, ref buffer);

                        if (stream.Read(buffer, 0, byteCount) == byteCount)
                            AddSegments(segments, buffer, byteCount);
                    }
                    else if (_directories[i].StreamType == MinidumpStreamType.Memory64ListStream)
                    {
                        MinidumpMemory64List memList64 = Read<MinidumpMemory64List>(stream, _directories[i].Rva);
                        int byteCount = ResizeBytesForArray<MinidumpMemoryDescriptor>(memList64.NumberOfMemoryRanges, ref buffer);

                        if (stream.Read(buffer, 0, byteCount) == byteCount)
                            AddSegments(segments, memList64.Rva, buffer, byteCount);
                    }
                }
            }
            finally
            {
                if (buffer != null)
                    ArrayPool<byte>.Shared.Return(buffer);
            }

            return segments.Where(s => s.Size > 0).OrderBy(s => s.VirtualAddress).ThenBy(s => s.Size).ToImmutableArray();
        }

        private static unsafe void AddSegments(List<MinidumpSegment> segments, byte[] buffer, int byteCount)
        {
            int count = byteCount / sizeof(MinidumpMemoryDescriptor);

            fixed (byte* ptr = buffer)
            {
                MinidumpMemoryDescriptor* desc = (MinidumpMemoryDescriptor*)ptr;
                for (int i = 0; i < count; i++)
                    segments.Add(new MinidumpSegment(desc[i].Rva, desc[i].StartAddress, desc[i].DataSize32));
            }
        }

        private static unsafe void AddSegments(List<MinidumpSegment> segments, ulong rva, byte[] buffer, int byteCount)
        {
            int count = byteCount / sizeof(MinidumpMemoryDescriptor);

            fixed (byte* ptr = buffer)
            {
                MinidumpMemoryDescriptor* desc = (MinidumpMemoryDescriptor*)ptr;
                for (int i = 0; i < count; i++)
                {
                    segments.Add(new MinidumpSegment(rva, desc[i].StartAddress, desc[i].DataSize64));
                    rva += desc[i].DataSize64;
                }
            }
        }

        private static unsafe int ResizeBytesForArray<T>(ulong count, [NotNull] ref byte[]? buffer)
            where T : unmanaged
        {
            int size = (int)count * sizeof(T);
            if (buffer == null)
            {
                buffer = new byte[size];
            }
            else if (buffer.Length < size)
            {
                ArrayPool<byte> pool = ArrayPool<byte>.Shared;

                pool.Return(buffer);
                buffer = pool.Rent(size);
            }

            return size;
        }

        private static async Task<T> ReadAsync<T>(Stream stream, byte[] buffer, long offset)
            where T : unmanaged
        {
            int size = SizeOf<T>();
            if (buffer.Length < size)
                buffer = new byte[size];

            stream.Position = offset;
            int read = await ReadAsync(stream, buffer, size).ConfigureAwait(false);
            if (read == size)
            {
                T result = Unsafe.As<byte, T>(ref buffer[0]);
                return result;
            }

            return default;
        }

        private static unsafe int SizeOf<T>() where T : unmanaged => sizeof(T);

        private static T Read<T>(Stream stream, long offset)
            where T : unmanaged
        {
            stream.Position = offset;
            return Read<T>(stream);
        }

        private static unsafe T Read<T>(Stream stream)
            where T : unmanaged
        {
            int size = sizeof(T);
            Span<byte> buffer = stackalloc byte[size];

            int read = stream.Read(buffer);
            if (read < size)
                return default;

            return Unsafe.As<byte, T>(ref buffer[0]);
        }

        private static bool Read<T>(Stream stream, long offset, T[] array)
            where T : unmanaged
        {
            stream.Position = offset;
            return Read(stream, array);
        }

        private static unsafe bool Read<T>(Stream stream, T[] array)
            where T : unmanaged
        {
            Span<byte> buffer = MemoryMarshal.AsBytes(new Span<T>(array));
            int read = stream.Read(buffer);
            return read == buffer.Length;
        }

        public override string ToString() => _displayName;

        private struct ThreadReadResult
        {
            public ImmutableArray<MinidumpContextData> ContextData;
            public ImmutableDictionary<uint, ulong> Tebs;
            public ImmutableArray<uint> Threads;
        }
    }

    internal readonly struct MinidumpContextData
    {
        public readonly uint ThreadId;
        public readonly uint ContextRva;
        public readonly uint ContextBytes;

        public MinidumpContextData(uint threadId, uint contextRva, uint contextBytes)
        {
            ThreadId = threadId;
            ContextRva = contextRva;
            ContextBytes = contextBytes;
        }
    }
}

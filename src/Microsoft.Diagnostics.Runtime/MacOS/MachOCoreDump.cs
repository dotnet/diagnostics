// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Diagnostics.Runtime.MacOS.Structs;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime.MacOS
{
    internal sealed unsafe class MachOCoreDump : IDisposable
    {
        private const uint X86_THREAD_STATE64 = 4;
        private const uint ARM_THREAD_STATE64 = 6;

        private readonly object _sync = new();
        private readonly Stream _stream;
        private readonly bool _leaveOpen;

        private readonly MachHeader64 _header;
        private readonly MachOSegment[] _segments;
        private MachOModule? _dylinker;

        private volatile Dictionary<ulong, MachOModule>? _modules;

        public ImmutableDictionary<uint, thread_state_t> Threads { get; }

        public uint ProcessId { get; }

        public Architecture Architecture => _header.CpuType switch
        {
            MachOCpuType.X86 => Architecture.X86,
            MachOCpuType.X86_64 => Architecture.X64,
            MachOCpuType.ARM => Architecture.Arm,
            MachOCpuType.ARM64 => Architecture.Arm64,
            _ => (Architecture)(-1)
        };

        public MachOCoreReader Parent { get; }

        public MachOCoreDump(MachOCoreReader parent, Stream stream, bool leaveOpen, string displayName)
        {
            Parent = parent;

            fixed (MachHeader64* header = &_header)
                if (stream.Read(new Span<byte>(header, sizeof(MachHeader64))) != sizeof(MachHeader64))
                    throw new IOException($"Failed to read header from {displayName}.");

            if (_header.Magic != MachHeader64.Magic64)
                throw new InvalidDataException($"'{displayName}' does not have a valid Mach-O header.");

            _stream = stream;
            _leaveOpen = leaveOpen;

            Dictionary<ulong, uint> threadIds = new();
            List<thread_state_t> contexts = new();
            List<MachOSegment> segments = new((int)_header.NumberCommands);

            for (int i = 0; i < _header.NumberCommands; i++)
            {
                long position = stream.Position;
                LoadCommandHeader loadCommand = default;
                stream.Read(new Span<byte>(&loadCommand, sizeof(LoadCommandHeader)));

                long next = position + loadCommand.Size;

                switch (loadCommand.Kind)
                {
                    case LoadCommandType.Segment64:
                        Segment64LoadCommand seg = default;
                        stream.Read(new Span<byte>(&seg, sizeof(Segment64LoadCommand)));

                        if (seg.VMAddr == SpecialThreadInfoHeader.SpecialThreadInfoAddress)
                        {
                            stream.Position = (long)seg.FileOffset;

                            SpecialThreadInfoHeader threadInfo = Read<SpecialThreadInfoHeader>(stream);
                            if (threadInfo.Signature != SpecialThreadInfoHeader.SpecialThreadInfoSignature)
                            {
                                segments.Add(new MachOSegment(seg));
                            }
                            else
                            {
                                for (int j = 0; j < threadInfo.NumberThreadEntries; j++)
                                {
                                    SpecialThreadInfoEntry threadEntry = Read<SpecialThreadInfoEntry>(stream);
                                    threadIds[threadEntry.StackPointer] = threadEntry.ThreadId;
                                }
                            }
                        }
                        else
                        {
                            segments.Add(new MachOSegment(seg));
                        }
                        break;

                    case LoadCommandType.Thread:
                        thread_state_t threadState = default;
                        uint flavor = Read<uint>(stream);
                        uint count = Read<uint>(stream);

                        switch (_header.CpuType)
                        {
                            case MachOCpuType.X86_64:
                                if (flavor == X86_THREAD_STATE64)
                                {
                                    threadState.x64 = Read<x86_thread_state64_t>(stream);
                                }
                                break;

                            case MachOCpuType.ARM64:
                                if (flavor == ARM_THREAD_STATE64)
                                {
                                    threadState.arm = Read<arm_thread_state64_t>(stream);
                                }
                                break;
                        }
                        contexts.Add(threadState);
                        break;
                }

                stream.Seek(next, SeekOrigin.Begin);
            }

            segments.Sort((x, y) => x.Address.CompareTo(y.Address));
            _segments = segments.ToArray();

            Dictionary<uint, thread_state_t> threadContexts = new();
            for (int i = 0; i < contexts.Count; i++)
            {
                ulong esp = default;
                switch (_header.CpuType)
                {
                    case MachOCpuType.X86_64:
                        esp = contexts[i].x64.__rsp;
                        break;
                    case MachOCpuType.ARM64:
                        esp = contexts[i].arm.__sp;
                        break;
                }
                if (threadIds.TryGetValue(esp, out uint threadId))
                {
                    threadContexts.Add(threadId, contexts[i]);
                }
                else
                {
                    // Use the index as the thread id if the special thread info memory section doesn't exists
                    threadContexts.Add((uint)i, contexts[i]);
                }
            }
            Threads = threadContexts.ToImmutableDictionary();
        }

        private static T Read<T>(Stream stream) where T : unmanaged
        {
            T value;
            stream.Read(new Span<byte>(&value, sizeof(T)));
            return value;
        }

        public MachOModule? GetModuleByBaseAddress(ulong baseAddress)
        {
            Dictionary<ulong, MachOModule> modules = ReadModules();

            modules.TryGetValue(baseAddress, out MachOModule? result);
            return result;
        }

        public IEnumerable<MachOModule> EnumerateModules()
        {
            return ReadModules().Values;
        }

        public T ReadMemory<T>(ulong address)
            where T : unmanaged
        {
            T t = default;

            int read = ReadMemory(address, new Span<byte>(&t, sizeof(T)));
            if (read == sizeof(T))
                return t;

            return default;
        }

        public int ReadMemory(ulong address, Span<byte> buffer)
        {
            if (address == 0)
                return 0;

            int read = 0;
            while (buffer.Length > 0 && FindSegmentContaining(address, out MachOSegment seg))
            {
                ulong offset = address - seg.Address;
                int len = Math.Min(buffer.Length, (int)(seg.Size - offset));
                if (len == 0)
                    break;

                long position = (long)(seg.FileOffset + offset);
                lock (_sync)
                {
                    _stream.Seek(position, SeekOrigin.Begin);
                    int count = _stream.Read(buffer.Slice(0, len));
                    if (count == 0)
                        break;

                    read += count;
                    address += (uint)count;
                    buffer = buffer.Slice(count);
                }
            }

            return read;
        }

        internal string ReadAscii(ulong address)
        {
            StringBuilder sb = new();

            int read = 0;
            Span<byte> buffer = new byte[32];

            while (true)
            {
                int count = ReadMemory(address + (uint)read, buffer);
                if (count <= 0)
                    return sb.ToString();

                foreach (byte b in buffer)
                    if (b == 0)
                        return sb.ToString();
                    else
                        sb.Append((char)b);

                read += count;
            }
        }

        private Dictionary<ulong, MachOModule> ReadModules()
        {
            if (_modules != null)
                return _modules;

            _dylinker ??= FindDylinker(firstPass: true) ?? FindDylinker(firstPass: false);

            if (_dylinker != null && _dylinker.TryLookupSymbol("dyld_all_image_infos", out ulong dyld_allImage_address))
            {
                DyldAllImageInfos allImageInfo = ReadMemory<DyldAllImageInfos>(dyld_allImage_address);
                DyldImageInfo[] allImages = new DyldImageInfo[allImageInfo.infoArrayCount];

                fixed (DyldImageInfo* ptr = allImages)
                {
                    int count = ReadMemory(allImageInfo.infoArray.ToUInt64(), new Span<byte>(ptr, sizeof(DyldImageInfo) * allImages.Length)) / sizeof(DyldImageInfo);

                    Dictionary<ulong, MachOModule> modules = new(count);
                    for (int i = 0; i < count; i++)
                    {
                        ref DyldImageInfo image = ref allImages[i];

                        string path = ReadAscii(image.ImageFilePath.ToUInt64());
                        ulong baseAddress = image.ImageLoadAddress.ToUInt64();
                        modules[baseAddress] = new MachOModule(this, baseAddress, path);
                    }

                    _modules = modules;
                    return modules;
                }
            }

            return _modules = new Dictionary<ulong, MachOModule>();
        }

        private MachOModule? FindDylinker(bool firstPass)
        {
            const uint skip = 0x1000;
            const uint firstPassAttemptCount = 8;
            foreach (MachOSegment seg in _segments)
            {
                ulong start = 0;
                ulong end = seg.FileSize;

                if (firstPass)
                    end = skip * firstPassAttemptCount;
                else
                    start = skip * firstPassAttemptCount;

                for (ulong offset = start; offset < end; offset += skip)
                {
                    MachHeader64 header = ReadMemory<MachHeader64>(seg.Address + offset);
                    if (header.Magic == MachHeader64.Magic64 && header.FileType == MachOFileType.Dylinker)
                    {
                        return new MachOModule(this, seg.Address + offset, "dylinker");
                    }
                }
            }
            return null;
        }

        private bool FindSegmentContaining(ulong address, out MachOSegment seg)
        {
            int lower = 0;
            int upper = _segments.Length - 1;

            while (lower <= upper)
            {
                int mid = (lower + upper) >> 1;
                ref MachOSegment curr = ref _segments[mid];

                if (address < curr.Address)
                {
                    upper = mid - 1;
                }
                else if (address >= curr.Address + curr.Size)
                {
                    lower = mid + 1;
                }
                else
                {
                    seg = curr;
                    return true;
                }
            }

            seg = default;
            return false;
        }

        public void Dispose()
        {
            if (!_leaveOpen)
                _stream.Dispose();
        }
    }
}

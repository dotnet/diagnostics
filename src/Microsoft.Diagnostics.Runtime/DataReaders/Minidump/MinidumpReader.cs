// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime.DataReaders.Implementation;
using Microsoft.Diagnostics.Runtime.Implementation;
using Microsoft.Diagnostics.Runtime.Windows;

namespace Microsoft.Diagnostics.Runtime
{
    internal sealed class MinidumpReader : IDataReader, IDisposable, IThreadReader, IDumpInfoProvider
    {
        private readonly Minidump _minidump;
        private IMemoryReader? _readerCached;

        public OSPlatform TargetPlatform => OSPlatform.Windows;

        public string DisplayName { get; }

        public IMemoryReader MemoryReader => _readerCached ??= _minidump.MemoryReader;

        public MinidumpReader(string displayName, Stream stream, CacheOptions cacheOptions, bool leaveOpen)
        {
            if (displayName is null)
                throw new ArgumentNullException(nameof(displayName));

            if (stream is null)
                throw new ArgumentNullException(nameof(stream));

            DisplayName = displayName;

            _minidump = new Minidump(displayName, stream, cacheOptions, leaveOpen);

            Architecture = _minidump.Architecture switch
            {
                MinidumpProcessorArchitecture.Amd64 => Architecture.X64,
                MinidumpProcessorArchitecture.Arm => Architecture.Arm,
                MinidumpProcessorArchitecture.Arm64 => Architecture.Arm64,
                MinidumpProcessorArchitecture.Intel => Architecture.X86,
                _ => throw new NotImplementedException($"No support for platform {_minidump.Architecture}"),
            };

            PointerSize = _minidump.PointerSize;
        }

        public bool IsThreadSafe => true;

        public Architecture Architecture { get; }

        public int ProcessId => _minidump.ProcessId;

        public int PointerSize { get; }

        public bool IsMiniOrTriage => _minidump.IsMiniDump;

        public void Dispose()
        {
            _minidump.Dispose();
        }

        public IEnumerable<ModuleInfo> EnumerateModules()
        {
            // We set buildId to "Empty" since only PEImages exist where minidumps are created, and we do not
            // want to try to lazily evaluate the buildId later
            return from module in _minidump.EnumerateModuleInfo()
                   select new PEModuleInfo(this, module.BaseOfImage, module.ModuleName ?? "", true, module.DateTimeStamp, module.SizeOfImage);
        }

        public void FlushCachedData()
        {
        }

        public IEnumerable<uint> EnumerateOSThreadIds() => _minidump.OrderedThreads;

        public ulong GetThreadTeb(uint osThreadId)
        {
            _minidump.Tebs.TryGetValue(osThreadId, out ulong teb);
            return teb;
        }

        public bool GetThreadContext(uint threadID, uint contextFlags, Span<byte> context)
        {
            int index = _minidump.ContextData.Search(threadID, (x, y) => x.ThreadId.CompareTo(y));
            if (index < 0)
                return false;

            MinidumpContextData ctx = _minidump.ContextData[index];
            if (ctx.ContextRva == 0 || ctx.ContextBytes == 0)
                return false;

            return _minidump.MemoryReader.ReadFromRva(ctx.ContextRva, context) == context.Length;
        }

        public int Read(ulong address, Span<byte> buffer) => MemoryReader.Read(address, buffer);
        public bool Read<T>(ulong address, out T value) where T : unmanaged => MemoryReader.Read(address, out value);
        public T Read<T>(ulong address) where T : unmanaged => MemoryReader.Read<T>(address);
        public bool ReadPointer(ulong address, out ulong value) => MemoryReader.ReadPointer(address, out value);
        public ulong ReadPointer(ulong address) => MemoryReader.ReadPointer(address);
    }
}
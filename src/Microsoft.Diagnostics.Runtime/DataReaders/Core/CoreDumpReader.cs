// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime.DataReaders.Implementation;
using Microsoft.Diagnostics.Runtime.Implementation;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime
{
    internal sealed class CoredumpReader : CommonMemoryReader, IDataReader, IDisposable, IThreadReader
    {
        private readonly ElfCoreFile _core;
        private Dictionary<uint, IElfPRStatus>? _threads;
        private List<ModuleInfo>? _modules;

        public string DisplayName { get; }
        public OSPlatform TargetPlatform => OSPlatform.Linux;

        public CoredumpReader(string path, Stream stream, bool leaveOpen)
        {
            DisplayName = path ?? throw new ArgumentNullException(nameof(path));
            _core = new ElfCoreFile(stream ?? throw new ArgumentNullException(nameof(stream)), leaveOpen);

            ElfMachine architecture = _core.ElfFile.Header.Architecture;
            (PointerSize, Architecture) = architecture switch
            {
                ElfMachine.EM_X86_64 => (8, Architecture.X64),
                ElfMachine.EM_386 => (4, Architecture.X86),
                ElfMachine.EM_AARCH64 => (8, Architecture.Arm64),
                ElfMachine.EM_ARM => (4, Architecture.Arm),
                _ => throw new NotImplementedException($"Support for {architecture} not yet implemented."),
            };
        }

        public bool IsThreadSafe => false;

        public void Dispose()
        {
            _core.Dispose();
        }

        public int ProcessId
        {
            get
            {
                foreach (IElfPRStatus status in _core.EnumeratePRStatus())
                    return (int)status.ProcessId;

                return -1;
            }
        }

        public IEnumerable<ModuleInfo> EnumerateModules()
        {
            if (_modules is null)
            {
                // Need to filter out non-modules like the interpreter (named something
                // like "ld-2.23") and anything that starts with /dev/ because their
                // memory range overlaps with actual modules.
                ulong interpreter = _core.GetAuxvValue(ElfAuxvType.Base);

                _modules = new List<ModuleInfo>();
                foreach (ElfLoadedImage image in _core.LoadedImages.Values)
                    if (image.BaseAddress != interpreter && !image.FileName.StartsWith("/dev", StringComparison.Ordinal))
                        _modules.Add(CreateModuleInfo(image));
            }

            return _modules;
        }

        private ModuleInfo CreateModuleInfo(ElfLoadedImage image)
        {
            using ElfFile? file = image.Open();

            // We suppress the warning because the function it wants us to use is not available on all ClrMD platforms

            // This substitution is for unloaded modules for which Linux appends " (deleted)" to the module name.
            string path = image.FileName.Replace(" (deleted)", "");
            if (file is not null)
            {
                long size = image.Size > long.MaxValue ? long.MaxValue : unchecked((long)image.Size);
                return new ElfModuleInfo(this, file, image.BaseAddress, size, path);
            }

            return new PEModuleInfo(this, image.BaseAddress, path, false);
        }

        public void FlushCachedData()
        {
            _threads = null;
            _modules = null;
        }

        public Architecture Architecture { get; }

        public override int PointerSize { get; }

        public IEnumerable<uint> EnumerateOSThreadIds()
        {
            foreach (IElfPRStatus status in _core.EnumeratePRStatus())
                yield return status.ThreadId;
        }

        public ulong GetThreadTeb(uint osThreadId) => 0;

        public bool GetThreadContext(uint threadID, uint contextFlags, Span<byte> context)
        {
            Dictionary<uint, IElfPRStatus> threads = LoadThreads();

            if (threads.TryGetValue(threadID, out IElfPRStatus? status))
                return status.CopyRegistersAsContext(context);

            return false;
        }

        public override int Read(ulong address, Span<byte> buffer)
        {
            DebugOnly.Assert(!buffer.IsEmpty);
            return address > long.MaxValue ? 0 : _core.ReadMemory(address, buffer);
        }

        private Dictionary<uint, IElfPRStatus> LoadThreads()
        {
            Dictionary<uint, IElfPRStatus>? threads = _threads;

            if (threads is null)
            {
                threads = new Dictionary<uint, IElfPRStatus>();
                foreach (IElfPRStatus status in _core.EnumeratePRStatus())
                    threads.Add(status.ThreadId, status);

                _threads = threads;
            }

            return threads;
        }
    }
}

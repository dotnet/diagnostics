// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime.DataReaders.Implementation;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime.MacOS
{
    internal sealed class MachOCoreReader : CommonMemoryReader, IDataReader, IThreadReader, IDisposable
    {
        private readonly MachOCoreDump _core;

        public string DisplayName { get; }

        public bool IsThreadSafe => true;

        public OSPlatform TargetPlatform => OSPlatform.OSX;

        public Architecture Architecture => _core.Architecture;

        public int ProcessId { get; }

        public unsafe MachOCoreReader(string displayName, Stream stream, bool leaveOpen)
        {
            DisplayName = displayName;
            _core = new MachOCoreDump(this, stream, leaveOpen, DisplayName);
        }

        public IEnumerable<ModuleInfo> EnumerateModules() => _core.EnumerateModules().Select(m => new MachOModuleInfo(m, m.BaseAddress, m.FileName, null, m.ImageSize));

        public void FlushCachedData()
        {
        }

        public IEnumerable<uint> EnumerateOSThreadIds() => _core.Threads.Keys;

        public ulong GetThreadTeb(uint _) => 0;

        public unsafe bool GetThreadContext(uint threadID, uint contextFlags, Span<byte> context)
        {
            if (!_core.Threads.TryGetValue(threadID, out thread_state_t thread))
                return false;

            switch (Architecture)
            {
                case Architecture.X64:
                    thread.x64.CopyContext(context);
                    break;
                case Architecture.Arm64:
                    thread.arm.CopyContext(context);
                    break;
                default:
                    throw new PlatformNotSupportedException();
            }
            return true;
        }

        public override int Read(ulong address, Span<byte> buffer) => _core.ReadMemory(address, buffer);

        public void Dispose()
        {
            _core.Dispose();
        }
    }
}
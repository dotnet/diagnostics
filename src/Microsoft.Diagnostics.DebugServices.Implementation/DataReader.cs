// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Runtime;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// ClrMD runtime service implementation
    /// </summary>
    internal class DataReader : IDataReader
    {
        private readonly ITarget _target;
        private IEnumerable<ModuleInfo> _modules;
        private IModuleService _moduleService;
        private IThreadService _threadService;
        private IMemoryService _memoryService;

        public DataReader(ITarget target)
        {
            _target = target;
            target.OnFlushEvent.Register(() => _modules = null);
        }

        #region IDataReader

        string IDataReader.DisplayName => "";

        bool IDataReader.IsThreadSafe => false;

        OSPlatform IDataReader.TargetPlatform => _target.OperatingSystem;

        Architecture IDataReader.Architecture => _target.Architecture;

        int IDataReader.ProcessId => unchecked((int)_target.ProcessId.GetValueOrDefault());

        IEnumerable<ModuleInfo> IDataReader.EnumerateModules() => _modules ??= ModuleService.EnumerateModules().Select((module) => new DataReaderModule(module)).ToList();

        bool IDataReader.GetThreadContext(uint threadId, uint contextFlags, Span<byte> context)
        {
            try
            {
                byte[] registerContext = ThreadService.GetThreadFromId(threadId).GetThreadContext();
                context = new Span<byte>(registerContext);
                return true;
            }
            catch (DiagnosticsException ex)
            {
                Trace.TraceError($"GetThreadContext: {threadId} exception {ex.Message}");
            }
            return false;
        }

        void IDataReader.FlushCachedData()
        {
        }

        #endregion

        #region IMemoryReader

        int IMemoryReader.PointerSize => MemoryService.PointerSize;

        int IMemoryReader.Read(ulong address, Span<byte> buffer)
        {
            MemoryService.ReadMemory(address, buffer, out int bytesRead);
            return bytesRead;
        }

        bool IMemoryReader.Read<T>(ulong address, out T value)
        {
            Span<byte> buffer = stackalloc byte[Marshal.SizeOf<T>()];
            if (((IMemoryReader)this).Read(address, buffer) == buffer.Length)
            {
                value = Unsafe.As<byte, T>(ref MemoryMarshal.GetReference(buffer));
                return true;
            }
            value = default;
            return false;
        }

        T IMemoryReader.Read<T>(ulong address)
        {
            ((IMemoryReader)this).Read(address, out T result);
            return result;
        }

        bool IMemoryReader.ReadPointer(ulong address, out ulong value)
        {
            return MemoryService.ReadPointer(address, out value);
        }

        ulong IMemoryReader.ReadPointer(ulong address)
        {
            MemoryService.ReadPointer(address, out ulong value);
            return value;
        }

        #endregion

        private IModuleService ModuleService => _moduleService ??= _target.Services.GetService<IModuleService>();

        private IMemoryService MemoryService => _memoryService ??= _target.Services.GetService<IMemoryService>();

        private IThreadService ThreadService => _threadService ??= _target.Services.GetService<IThreadService>();

        private class DataReaderModule : ModuleInfo
        {
            private readonly IModule _module;

            public DataReaderModule(IModule module)
                : base(module.ImageBase, module.FileName)
            {
                _module = module;
            }

            public override long ImageSize => unchecked((long)_module.ImageSize);

            public override int IndexFileSize => unchecked((int)_module.IndexFileSize.GetValueOrDefault(0));

            public override int IndexTimeStamp => unchecked((int)_module.IndexTimeStamp.GetValueOrDefault(0));

            public override ModuleKind Kind => ModuleKind.Unknown;

            public override Version Version
            {
                get 
                {
                    try
                    {
                        return _module.GetVersionData() ?? Utilities.EmptyVersion;
                    }
                    catch (DiagnosticsException ex)
                    {
                        Trace.TraceError($"ModuleInfo.Version: {_module.ImageBase:X16} exception {ex.Message}");
                    }
                    return Utilities.EmptyVersion;
                }
            }

            public override ImmutableArray<byte> BuildId
            {
                get
                {
                    try
                    {
                        return _module.BuildId;
                    }
                    catch (DiagnosticsException ex)
                    {
                        Trace.TraceError($"ModuleInfo.BuildId: {_module.ImageBase:X16} exception {ex.Message}");
                    }
                    return ImmutableArray<byte>.Empty;
                }
            }

            public override PdbInfo Pdb 
            {
                get
                {
                    try
                    {
                        PdbFileInfo pdbFileInfo = _module.GetPdbFileInfos().Where((pdbFileInfo) => pdbFileInfo.IsPortable).LastOrDefault();
                        if (pdbFileInfo is null)
                        {
                            pdbFileInfo = _module.GetPdbFileInfos().LastOrDefault();
                            if (pdbFileInfo is null)
                            {
                                return default;
                            }
                        }
                        return new PdbInfo(pdbFileInfo.Path, pdbFileInfo.Guid, pdbFileInfo.Revision);
                    }
                    catch (DiagnosticsException ex)
                    {
                        Trace.TraceError($"ModuleInfo.Pdb: {_module.ImageBase:X16} exception {ex.Message}");
                    }
                    return default;
                }
            }

            public override bool IsManaged => _module.IsManaged;

            public override ulong GetExportSymbolAddress(string symbol)
            {
                var exportSymbols = _module.Services.GetService<IExportSymbols>();
                if (exportSymbols is not null)
                {
                    if (exportSymbols.TryGetSymbolAddress(symbol, out ulong offset))
                    {
                        return offset;
                    }
                }
                return 0;
            }

            public override IResourceNode ResourceRoot => base.ResourceRoot;
        }
    }
}

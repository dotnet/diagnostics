// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// ClrMD runtime service implementation. This MUST never be disposable.
    /// </summary>
    [ServiceExport(Type = typeof(IDataReader), Scope = ServiceScope.Target)]
    public class DataReader : IDataReader
    {
        private readonly ITarget _target;
        private IEnumerable<ModuleInfo> _modules;

        [ServiceImport]
        private IModuleService ModuleService { get; set; }

        [ServiceImport]
        private IMemoryService MemoryService { get; set; }

        [ServiceImport]
        private IThreadService ThreadService { get; set; }

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

        IEnumerable<ModuleInfo> IDataReader.EnumerateModules() => _modules ??= ModuleService.EnumerateModules().Select((module) => new DataReaderModule(this, module)).ToList();

        bool IDataReader.GetThreadContext(uint threadId, uint contextFlags, Span<byte> context)
        {
            try
            {
                byte[] registerContext = ThreadService.GetThreadFromId(threadId).GetThreadContext();
                registerContext.AsSpan().Slice(0, context.Length).CopyTo(context);
                return true;
            }
            catch (Exception ex) when (ex is DiagnosticsException or ArgumentException)
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
            Span<byte> buffer = stackalloc byte[Unsafe.SizeOf<T>()];
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

        private sealed class DataReaderModule : ModuleInfo
        {
            private readonly IDataReader _reader;
            private readonly IModule _module;
            private IResourceNode _resourceRoot;

            public DataReaderModule(IDataReader reader, IModule module)
                : base(module.ImageBase, module.FileName)
            {
                _reader = reader;
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
                IExportSymbols exportSymbols = _module.Services.GetService<IExportSymbols>();
                if (exportSymbols is not null)
                {
                    if (exportSymbols.TryGetSymbolAddress(symbol, out ulong offset))
                    {
                        return offset;
                    }
                }
                return 0;
            }

            public override IResourceNode ResourceRoot => _resourceRoot ??= ModuleInfo.TryCreateResourceRoot(_reader, _module.ImageBase, _module.ImageSize, _module.IsFileLayout.GetValueOrDefault(false));
        }
    }
}

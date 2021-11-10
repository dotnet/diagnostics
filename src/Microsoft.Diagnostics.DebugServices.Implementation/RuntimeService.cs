// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.Runtime;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Architecture = System.Runtime.InteropServices.Architecture;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// ClrMD runtime service implementation
    /// </summary>
    public class RuntimeService : IRuntimeService, IDataReader, IExportReader
    {
        private readonly ITarget _target;
        private readonly IDisposable _onFlushEvent;
        private DataTarget _dataTarget;
        private List<Runtime> _runtimes;
        private IContextService _contextService;
        private IModuleService _moduleService;
        private IThreadService _threadService;
        private IMemoryService _memoryService;

        public RuntimeService(ITarget target)
        {
            _target = target;
            _onFlushEvent = target.OnFlushEvent.Register(() =>
            {
                if (_runtimes is not null && _runtimes.Count == 0)
                {
                    // If there are no runtimes, try find them again when the target stops
                    _runtimes = null;
                    _dataTarget?.Dispose();
                    _dataTarget = null;
                }
            });
            // Can't make RuntimeService IDisposable directly because _dataTarget.Dispose() disposes the IDataReader 
            // passed which is this RuntimeService instance which would call _dataTarget.Dispose again and causing a 
            // stack overflow.
            target.OnDestroyEvent.Register(() =>
            {
                _dataTarget?.Dispose();
                _dataTarget = null;
                _onFlushEvent.Dispose();
            });
        }

        #region IRuntimeService

        /// <summary>
        /// Returns the list of runtimes in the target
        /// </summary>
        public IEnumerable<IRuntime> EnumerateRuntimes()
        {
            if (_runtimes is null)
            {
                _runtimes = new List<Runtime>();
                if (_dataTarget is null)
                {
                    _dataTarget = new DataTarget(new CustomDataTarget(this))
                    {
                        BinaryLocator = null
                    };
                }
                if (_dataTarget is not null)
                {
                    for (int i = 0; i < _dataTarget.ClrVersions.Length; i++)
                    {
                        _runtimes.Add(new Runtime(_target, i, _dataTarget.ClrVersions[i]));
                    }
                }
            }
            return _runtimes;
        }

        #endregion

        #region IDataReader

        string IDataReader.DisplayName => "";

        bool IDataReader.IsThreadSafe => false;

        OSPlatform IDataReader.TargetPlatform => _target.OperatingSystem;

        Microsoft.Diagnostics.Runtime.Architecture IDataReader.Architecture
        {
            get
            {
                return _target.Architecture switch
                {
                    Architecture.X64 => Microsoft.Diagnostics.Runtime.Architecture.Amd64,
                    Architecture.X86 => Microsoft.Diagnostics.Runtime.Architecture.X86,
                    Architecture.Arm => Microsoft.Diagnostics.Runtime.Architecture.Arm,
                    Architecture.Arm64 => Microsoft.Diagnostics.Runtime.Architecture.Arm64,
                    _ => throw new PlatformNotSupportedException($"{_target.Architecture}"),
                };
            }
        }

        int IDataReader.ProcessId => unchecked((int)_target.ProcessId.GetValueOrDefault());

        IEnumerable<ModuleInfo> IDataReader.EnumerateModules() =>
            ModuleService.EnumerateModules().Select((module) => CreateModuleInfo(module)).ToList();

        private ModuleInfo CreateModuleInfo(IModule module) =>
            new ModuleInfo(
                this,
                module.ImageBase,
                module.FileName,
                isVirtual: true,
                unchecked((int)module.IndexFileSize.GetValueOrDefault(0)),
                unchecked((int)module.IndexTimeStamp.GetValueOrDefault(0)),
                new ImmutableArray<byte>());

        ImmutableArray<byte> IDataReader.GetBuildId(ulong baseAddress)
        {
            try
            {
                return ModuleService.GetModuleFromBaseAddress(baseAddress).BuildId;
            }
            catch (DiagnosticsException ex)
            {
                Trace.TraceError($"GetBuildId: {baseAddress:X16} exception {ex.Message}");
            }
            return ImmutableArray<byte>.Empty;
        }

        bool IDataReader.GetVersionInfo(ulong baseAddress, out Microsoft.Diagnostics.Runtime.VersionInfo version)
        {
            try
            {
                VersionData versionData = ModuleService.GetModuleFromBaseAddress(baseAddress).VersionData;
                if (versionData is not null)
                {
                    version = versionData.ToVersionInfo();
                    return true;
                }
            }
            catch (DiagnosticsException ex)
            {
                Trace.TraceError($"GetVersionInfo: {baseAddress:X16} exception {ex.Message}");
            }
            version = default;
            return false;
        }

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

        #region IExportReader

        bool IExportReader.TryGetSymbolAddress(ulong baseAddress, string name, out ulong offset)
        {
            try
            {
                IExportSymbols exportSymbols = ModuleService.GetModuleFromBaseAddress(baseAddress).Services.GetService<IExportSymbols>();
                if (exportSymbols is not null)
                {
                    return exportSymbols.TryGetSymbolAddress(name, out offset);
                }
            }
            catch (DiagnosticsException)
            {
            }
            offset = 0;
            return false;
        }

        #endregion

        private IRuntime CurrentRuntime => ContextService.Services.GetService<IRuntime>();

        private IContextService ContextService => _contextService ??= _target.Services.GetService<IContextService>();

        private IModuleService ModuleService => _moduleService ??= _target.Services.GetService<IModuleService>();

        private IMemoryService MemoryService => _memoryService ??= _target.Services.GetService<IMemoryService>();

        private IThreadService ThreadService => _threadService ??= _target.Services.GetService<IThreadService>();

        public override string ToString()
        {
            var sb = new StringBuilder();
            if (_runtimes is not null)
            {
                foreach (IRuntime runtime in _runtimes)
                {
                    string current = _runtimes.Count > 1 ? runtime == CurrentRuntime ? "*" : " " : "";
                    sb.Append(current);
                    sb.AppendLine(runtime.ToString());
                }
            }
            return sb.ToString();
        }
    }
}

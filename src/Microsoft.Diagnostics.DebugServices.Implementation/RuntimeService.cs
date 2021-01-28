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
using System.Text;
using Architecture = System.Runtime.InteropServices.Architecture;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// ClrMD runtime service implementation
    /// </summary>
    public class RuntimeService : IRuntimeService, IDataReader
    {
        private readonly ITarget _target;
        private readonly IDisposable _onFlushEvent;
        private DataTarget _dataTarget;
        private string _runtimeModuleDirectory;
        private List<Runtime> _runtimes;
        private Runtime _currentRuntime;
        private IModuleService _moduleService;
        private IThreadService _threadService;
        private IMemoryService _memoryService;

        public RuntimeService(ITarget target)
        {
            _target = target;
            _onFlushEvent = target.OnFlushEvent.Register(() => {
                if (_runtimes != null && _runtimes.Count == 0)
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
            target.DisposeOnClose(new UnregisterCallback(() => {
                _dataTarget?.Dispose();
                _dataTarget = null;
                _onFlushEvent.Dispose();
            }));
        }

        #region IRuntimeService

        /// <summary>
        /// Directory of the runtime module (coreclr.dll, libcoreclr.so, etc.)
        /// </summary>
        public string RuntimeModuleDirectory
        {
            get { return _runtimeModuleDirectory; }
            set
            {
                _runtimeModuleDirectory = value;
                _runtimes = null;
                _currentRuntime = null;
            }
        }

        /// <summary>
        /// Returns the list of runtimes in the target
        /// </summary>
        public IEnumerable<IRuntime> EnumerateRuntimes() => BuildRuntimes();

        /// <summary>
        /// Returns the current runtime
        /// </summary>
        public IRuntime CurrentRuntime
        {
            get
            {
                if (_currentRuntime == null) {
                    _currentRuntime = FindRuntime();
                }
                return _currentRuntime;
            }
        }

        /// <summary>
        /// Set the current runtime 
        /// </summary>
        /// <param name="runtimeId">runtime id</param>
        public void SetCurrentRuntime(int runtimeId)
        {
            if (_runtimes == null || runtimeId >= _runtimes.Count) {
                throw new DiagnosticsException($"Invalid runtime id {runtimeId}");
            }
            _currentRuntime = _runtimes[runtimeId];
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
                isVirtual:true,
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

        bool IDataReader.GetVersionInfo(ulong baseAddress, out VersionInfo version)
        {
            try
            {
                VersionInfo? v = ModuleService.GetModuleFromBaseAddress(baseAddress).Version;
                if (v.HasValue)
                {
                    version = v.Value;
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

        void IDataReader.FlushCachedData() => _target.Flush();

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

        /// <summary>
        /// Find the runtime
        /// </summary>
        private Runtime FindRuntime()
        {
            IEnumerable<Runtime> runtimes = BuildRuntimes();
            Runtime runtime = null;

            // First check if there is a .NET Core runtime loaded
            foreach (Runtime r in runtimes)
            {
                if (r.RuntimeType == RuntimeType.NetCore || r.RuntimeType == RuntimeType.SingleFile)
                {
                    runtime = r;
                    break;
                }
            }
            // If no .NET Core runtime, then check for desktop runtime
            if (runtime == null)
            {
                foreach (Runtime r in runtimes)
                {
                    if (r.RuntimeType == RuntimeType.Desktop)
                    {
                        runtime = r;
                        break;
                    }
                }
            }
            return runtime;
        }

        private IEnumerable<Runtime> BuildRuntimes()
        {
            if (_runtimes == null)
            {
                _runtimes = new List<Runtime>();
                if (_dataTarget == null)
                {
                    _dataTarget = new DataTarget(new CustomDataTarget(this) {
                        BinaryLocator = new BinaryLocator(_target)
                    });
                }
                if (_dataTarget != null)
                {
                    for (int i = 0; i < _dataTarget.ClrVersions.Length; i++)
                    {
                        _runtimes.Add(new Runtime(_target, this, _dataTarget.ClrVersions[i], i));
                    }
                }
            }
            return _runtimes;
        }

        private IModuleService ModuleService
        {
            get
            {
                if (_moduleService == null)
                {
                    _moduleService = _target.Services.GetService<IModuleService>();
                }
                return _moduleService;
            }
        }

        private IMemoryService MemoryService
        {
            get
            {
                if (_memoryService == null)
                {
                    _memoryService = _currentRuntime?.Services.GetService<IMemoryService>() ?? _target.Services.GetService<IMemoryService>();
                }
                return _memoryService;
            }
        }

        private IThreadService ThreadService
        {
            get
            {
                if (_threadService == null)
                {
                    _threadService = _target.Services.GetService<IThreadService>();
                }
                return _threadService;
            }
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            if (_runtimeModuleDirectory != null) {
                sb.AppendLine($"Runtime module path: {_runtimeModuleDirectory}");
            }
            if (_runtimes != null)
            {
                foreach (IRuntime runtime in _runtimes)
                {
                    string current = _runtimes.Count > 1 ? runtime == _currentRuntime ? "*" : " " : "";
                    sb.Append(current);
                    sb.AppendLine(runtime.ToString());
                }
            }
            return sb.ToString();
        }
    }
}

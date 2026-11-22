// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Xunit;

namespace Microsoft.Diagnostics.DebugServices.UnitTests
{
    // Keep SOS's service wiring, IDataReader adapter, and runtime discovery real.
    // Only the debugger's modules/memory and external symbol-file downloads are simulated.
    internal sealed class RuntimeDiscoveryTestTarget : IDisposable
    {
        private readonly Host _host = new(HostType.DotnetDump);
        private readonly List<ModuleDefinition> _modules = [];
        private readonly TestMemory _memory;
        private readonly TestTarget _target;

        public RecordingSymbolService Symbols { get; } = new();
        public List<(ulong ModuleBase, string Symbol)> ExportProbes { get; } = [];
        public IServiceProvider Services => _target.Services;
        public IRuntimeService Runtimes => _target.Services.GetService<IRuntimeService>();
        public int CrashInfoRequests => _target.CrashInfoRequests;

        public RuntimeDiscoveryTestTarget(OSPlatform platform, Architecture architecture, Action<ServiceManager> configureProviders = null)
        {
            _memory = new(architecture is Architecture.X86 or Architecture.Arm ? 4 : 8);
            configureProviders?.Invoke(_host.ServiceManager);
            ServiceContainer hostServices = _host.CreateServiceContainer();
            hostServices.AddService<ISymbolService>(Symbols);
            hostServices.AddService<IContextService>(new ContextService(_host));
            _target = new(_host, platform, architecture, _memory,
                services => new TestModules(services, _modules, Symbols, ExportProbes));
        }

        public ModuleDefinition AddModule(ulong imageBase, string fileName, bool entryPoint = false, bool needsSymbols = false)
        {
            ModuleDefinition module = new(imageBase, fileName, entryPoint, needsSymbols);
            _modules.Add(module);
            return module;
        }

        public void AddCoreRuntime(ulong imageBase, bool diagnosticHeader = true, string fileName = null)
        {
            string name = fileName ?? (_target.OperatingSystem == OSPlatform.Windows ? @"C:\runtime\coreclr.dll"
                : _target.OperatingSystem == OSPlatform.Linux ? "/runtime/libcoreclr.so"
                : "/runtime/libcoreclr.dylib");
            ModuleDefinition core = AddModule(imageBase, name);
            core.Exports.Add("DotNetRuntimeContractDescriptor", core.ImageBase + 0x100);
            if (diagnosticHeader)
            {
                AddDiagnosticHeader(core.ImageBase);
            }
        }

        public void AddDiagnosticHeader(ulong runtimeBase, int version = 2, bool legacyMacAddress = false)
        {
            byte[] header = new byte[40];
            "DIAGINFOHEADER"u8.CopyTo(header);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16), version);
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(32), runtimeBase);
            ulong address = _target.OperatingSystem == OSPlatform.OSX && legacyMacAddress
                ? 0x7fffffff10000000UL
                : _memory.PointerSize == 4 ? 0x7fff1000UL : 0x00007ffffff10000UL;
            _memory.Add(address, header);
        }

        public void AddCrashInfo(ulong runtimeBase) => _target.CrashRuntimeBase = runtimeBase;

        public byte[] AddSingleFileRuntime(ModuleDefinition module, bool symbolOnly = false, int headerSize = 112)
        {
            // RuntimeInfo uses Pack=4: signature[18], padding, version, three 24-byte
            // identity slots, followed by four version integers for header version 2.
            byte[] header = new byte[112];
            "DotNetRuntimeInfo"u8.CopyTo(header);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(20), 2);
            int identity = 1;
            foreach (int offset in new[] { 24, 48, 72 })
            {
                header[offset] = 8;
                BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(offset + 1), 0x12345678 + identity * 0x11111111);
                BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(offset + 5), 0x1000 + identity * 0x1000);
                identity++;
            }
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(96), 11);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(100), 2);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(104), 3);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(108), 4);
            Array.Resize(ref header, headerSize);
            ulong address = module.ImageBase + 0x200;
            _memory.Add(address, header);
            (symbolOnly ? module.Symbols : module.Exports).Add("DotNetRuntimeInfo", address);
            return header;
        }

        public IRuntime[] Enumerate(RuntimeEnumerationFlags flags = RuntimeEnumerationFlags.Default) => Runtimes.EnumerateRuntimes(flags).ToArray();

        public void Flush() => _target.Flush();

        public void Dispose() => _host.DestoryTargets();

        internal sealed record ModuleDefinition(ulong ImageBase, string FileName, bool EntryPoint, bool NeedsSymbols)
        {
            public Dictionary<string, ulong> Exports { get; } = [];
            public Dictionary<string, ulong> Symbols { get; } = [];
        }

        private sealed class TestTarget : Target
        {
            public ulong? CrashRuntimeBase { get; set; }
            public int CrashInfoRequests { get; private set; }

            public TestTarget(Host host, OSPlatform platform, Architecture architecture, IMemoryService memory, ServiceFactory modules)
                : base(host, dumpPath: null)
            {
                OperatingSystem = platform;
                Architecture = architecture;
                IsDump = true;
                ProcessId = 1;
                _serviceContainerFactory.AddServiceFactory<IModuleService>(modules);
                _serviceContainerFactory.AddServiceFactory<IMemoryService>(_ => memory);
                _serviceContainerFactory.AddServiceFactory<IThreadService>(_ => new NoThreads());
                _serviceContainerFactory.AddServiceFactory<ICrashInfoService>(services => {
                    CrashInfoRequests++;
                    return CrashRuntimeBase is ulong runtimeBase
                        ? CrashInfoService.Create(0, Encoding.UTF8.GetBytes(
                            $"{{\"version\":\"1.0\",\"runtime_base\":\"0x{runtimeBase:X}\"}}"), services.GetService<IModuleService>())
                        : null;
                });
                OnFlushEvent.Register(() => FlushService<ICrashInfoService>());
                Finished();
            }
        }

        private sealed class TestModules(
            IServiceProvider services,
            List<ModuleDefinition> definitions,
            RecordingSymbolService symbols,
            List<(ulong ModuleBase, string Symbol)> probes) : ModuleService(services)
        {
            public override IModule EntryPointModule => definitions.SingleOrDefault(module => module.EntryPoint) is ModuleDefinition entryPoint
                ? ((IModuleService)this).GetModuleFromBaseAddress(entryPoint.ImageBase)
                : null;

            protected override Dictionary<ulong, IModule> GetModulesInner() => definitions.ToDictionary(
                definition => definition.ImageBase,
                definition => (IModule)new TestModule(this, definition, symbols, probes));
        }

        private sealed class TestModule : Module
        {
            private readonly TestModules _moduleService;
            private readonly ModuleDefinition _definition;
            private readonly RecordingSymbolService _symbols;
            private readonly List<(ulong ModuleBase, string Symbol)> _probes;
            private bool _symbolsRequested;
            private string _symbolPath;

            public TestModule(TestModules modules, ModuleDefinition definition, RecordingSymbolService symbols, List<(ulong ModuleBase, string Symbol)> probes)
                : base(modules.Services, isPEModuleProbeSupported: false)
            {
                _moduleService = modules;
                _definition = definition;
                _symbols = symbols;
                _probes = probes;
                ImageBase = definition.ImageBase;
                ImageSize = 0x1000;
                FileName = definition.FileName;
                if (Target.OperatingSystem == OSPlatform.Windows)
                {
                    IndexTimeStamp = 0x12345678;
                    IndexFileSize = 0x1000;
                }
            }

            protected override ModuleService ModuleService => _moduleService;
            public override bool IsPEImage => Target.OperatingSystem == OSPlatform.Windows;
            public override ImmutableArray<byte> BuildId =>
                Target.OperatingSystem == OSPlatform.Windows ? ImmutableArray<byte>.Empty : [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];
            public override Version GetVersionData() => new(11, 0, 1, 2);
            public override string GetVersionString() => "11.0.1.2";

            public override string LoadSymbols()
            {
                // Model a debugger's per-module symbol-load cache, including a cached miss.
                if (!_symbolsRequested)
                {
                    _symbolsRequested = true;
                    _symbolPath = _symbols.DownloadSymbolFile(this);
                }
                return _symbolPath;
            }

            protected override bool TryGetSymbolAddressInner(string name, out ulong address)
            {
                _probes.Add((ImageBase, name));
                // TODO: Verify on Windows, Linux, and macOS that real export lookup can resolve
                // exports without contacting a symbol server, as this branch assumes.
                if (_definition.Exports.TryGetValue(name, out address))
                {
                    return true;
                }
                if (_definition.NeedsSymbols && LoadSymbols() is not null)
                {
                    return _definition.Symbols.TryGetValue(name, out address);
                }
                address = 0;
                return false;
            }
        }

        private sealed class TestMemory(int pointerSize) : IMemoryService
        {
            private readonly Dictionary<ulong, byte[]> _regions = [];
            public int PointerSize => pointerSize;
            public void Add(ulong address, byte[] bytes) => _regions.Add(address, bytes);

            public bool ReadMemory(ulong address, Span<byte> buffer, out int bytesRead)
            {
                foreach ((ulong start, byte[] bytes) in _regions)
                {
                    if (address >= start && address - start < (ulong)bytes.Length)
                    {
                        int offset = (int)(address - start);
                        bytesRead = Math.Min(buffer.Length, bytes.Length - offset);
                        bytes.AsSpan(offset, bytesRead).CopyTo(buffer);
                        return bytesRead > 0;
                    }
                }
                bytesRead = 0;
                return false;
            }

            public bool WriteMemory(ulong address, Span<byte> buffer, out int bytesWritten) => throw new NotSupportedException();
        }

        private sealed class NoThreads : IThreadService
        {
            public IEnumerable<RegisterInfo> Registers => [];
            public int InstructionPointerIndex => throw new NotSupportedException();
            public int FramePointerIndex => throw new NotSupportedException();
            public int StackPointerIndex => throw new NotSupportedException();
            public bool TryGetRegisterIndexByName(string name, out int registerIndex) => throw new NotSupportedException();
            public bool TryGetRegisterInfo(int registerIndex, out RegisterInfo info) => throw new NotSupportedException();
            public IEnumerable<IThread> EnumerateThreads() => [];
            public IThread GetThreadFromIndex(int threadIndex) => throw new NotSupportedException();
            public IThread GetThreadFromId(uint threadId) => throw new NotSupportedException();
        }

        internal sealed class RecordingSymbolService : ISymbolService
        {
            private readonly Queue<(ulong ModuleBase, string Result)> _expected = [];
            public List<ulong> Requests { get; } = [];
            public void ExpectSymbolFile(ulong moduleBase, string result) => _expected.Enqueue((moduleBase, result));
            public void Verify() => Assert.Empty(_expected);

            public string DownloadSymbolFile(IModule module)
            {
                Requests.Add(module.ImageBase);
                Assert.True(_expected.Count > 0, $"Unexpected symbol-file request for {module.ImageBase:X16} ({module.FileName})");
                (ulong moduleBase, string result) = _expected.Dequeue();
                Assert.Equal(moduleBase, module.ImageBase);
                return result;
            }

            public string DownloadModuleFile(IModule module) => throw new Xunit.Sdk.XunitException($"Unexpected module-file request for {module.ImageBase:X16} ({module.FileName})");

            // Unset by default so unexpected symbol-store file requests fail the test.
            public Func<string, string, string> FileDownloader { get; set; }
            public List<(string Index, string File)> FileRequests { get; } = [];

            public string DownloadFile(string index, string file)
            {
                if (FileDownloader is null)
                {
                    throw new Xunit.Sdk.XunitException($"Unexpected file request for {index} ({file})");
                }
                FileRequests.Add((index, file));
                return FileDownloader(index, file);
            }

            public IServiceEvent OnChangeEvent { get; } = new ServiceEvent();
            public bool IsSymbolStoreEnabled => true;
            public string DefaultSymbolPath => throw new NotSupportedException();
            public string DefaultSymbolCache => throw new NotSupportedException();
            public int DefaultTimeout => throw new NotSupportedException();
            public int DefaultRetryCount => throw new NotSupportedException();
            public void Reset() => throw new NotSupportedException();
            public bool ParseSymbolPath(string path) => throw new NotSupportedException();
            public bool AddSymwebSymbolServer(bool includeInteractiveCredentials = false, int? timeoutInMinutes = null, int? retryCount = null) => throw new NotSupportedException();
            public bool AddAuthenticatedSymbolServer(string accessToken, string symbolServerPath = null, int? timeoutInMinutes = null, int? retryCount = null) => throw new NotSupportedException();
            public bool AddSymbolServer(string symbolServerPath = null, int? timeoutInMinutes = null, int? retryCount = null) => throw new NotSupportedException();
            public void AddCachePath(string path) => throw new NotSupportedException();
            public void AddDirectoryPath(string path) => throw new NotSupportedException();
            public void DisableSymbolStore() => throw new NotSupportedException();
            public ISymbolFile OpenSymbolFile(string path, bool isFileLayout, Stream stream) => throw new NotSupportedException();
            public ISymbolFile OpenSymbolFile(Stream stream) => throw new NotSupportedException();
        }
    }
}

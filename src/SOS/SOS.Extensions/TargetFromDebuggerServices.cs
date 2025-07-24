// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Microsoft.Diagnostics.Runtime.Utilities;
using SOS.Extensions.Clrma;
using SOS.Hosting;
using SOS.Hosting.DbgEng.Interop;
using Architecture = System.Runtime.InteropServices.Architecture;

namespace SOS.Extensions
{
    /// <summary>
    /// ITarget implementation for the ClrMD IDataReader
    /// </summary>
    internal sealed class TargetFromDebuggerServices : Target
    {
        /// <summary>
        /// Build a target instance from IDataReader
        /// </summary>
        internal TargetFromDebuggerServices(DebuggerServices debuggerServices, IHost host)
            : base(host, dumpPath: null)
        {
            Debug.Assert(debuggerServices != null);

            HResult hr = debuggerServices.GetOperatingSystem(out DebuggerServices.OperatingSystem operatingSystem);
            Debug.Assert(hr == HResult.S_OK);
            OperatingSystem = operatingSystem switch
            {
                DebuggerServices.OperatingSystem.Windows => OSPlatform.Windows,
                DebuggerServices.OperatingSystem.Linux => OSPlatform.Linux,
                DebuggerServices.OperatingSystem.OSX => OSPlatform.OSX,
                _ => throw new PlatformNotSupportedException($"Operating system not supported: {operatingSystem}"),
            };

            hr = debuggerServices.GetDebuggeeType(out DEBUG_CLASS debugClass, out DEBUG_CLASS_QUALIFIER qualifier);
            Debug.Assert(hr == HResult.S_OK);
            if (qualifier >= DEBUG_CLASS_QUALIFIER.USER_WINDOWS_SMALL_DUMP)
            {
                IsDump = true;
            }

            hr = debuggerServices.GetProcessorType(out IMAGE_FILE_MACHINE type);
            if (hr == HResult.S_OK)
            {
                Debug.Assert(type is not IMAGE_FILE_MACHINE.ARM64X and not IMAGE_FILE_MACHINE.ARM64EC);
                Architecture = type switch
                {
                    IMAGE_FILE_MACHINE.I386 => Architecture.X86,
                    IMAGE_FILE_MACHINE.ARM => Architecture.Arm,
                    IMAGE_FILE_MACHINE.THUMB => Architecture.Arm,
                    IMAGE_FILE_MACHINE.ARMNT => Architecture.Arm,
                    IMAGE_FILE_MACHINE.AMD64 => Architecture.X64,
                    IMAGE_FILE_MACHINE.ARM64 => Architecture.Arm64,
                    IMAGE_FILE_MACHINE.LOONGARCH64 => (Architecture)6 /* Architecture.LoongArch64 */,
                    IMAGE_FILE_MACHINE.RISCV64 => (Architecture)9 /* Architecture.RiscV64 */,
                    _ => throw new PlatformNotSupportedException($"Machine type not supported: {type}"),
                };
            }
            else
            {
                throw new PlatformNotSupportedException($"GetProcessorType() FAILED {hr:X8}");
            }

            hr = debuggerServices.GetCurrentProcessId(out uint processId);
            if (hr == HResult.S_OK)
            {
                ProcessId = processId;
            }
            else
            {
                Trace.TraceError("GetCurrentThreadId() FAILED {0:X8}", hr);
            }

            // Add the thread, memory, and module services
            _serviceContainerFactory.AddServiceFactory<IModuleService>((services) => new ModuleServiceFromDebuggerServices(services, debuggerServices));
            _serviceContainerFactory.AddServiceFactory<IThreadService>((services) => new ThreadServiceFromDebuggerServices(services, debuggerServices));
            _serviceContainerFactory.AddServiceFactory<IMemoryService>((_) => {
                Debug.Assert(Host.HostType != HostType.DotnetDump);
                IMemoryService memoryService = new MemoryServiceFromDebuggerServices(this, debuggerServices);
                if (IsDump && Host.HostType == HostType.Lldb)
                {
                    ServiceContainerFactory clone = _serviceContainerFactory.Clone();
                    clone.RemoveServiceFactory<IMemoryService>();

                    // lldb doesn't map managed modules into the address space
                    memoryService = new ImageMappingMemoryService(clone.Build(), memoryService, managed: true);

                    // This is a special memory service that maps the managed assemblies' metadata into the address
                    // space. The lldb debugger returns zero's (instead of failing the memory read) for missing pages
                    // in core dumps that older (< 5.0) createdumps generate so it needs this special metadata mapping
                    // memory service. dotnet-dump needs this logic for clrstack -i (uses ICorDebug data targets).
                    memoryService = new MetadataMappingMemoryService(clone.Build(), memoryService);
                }
                return memoryService;
            });

            // Add optional crash info service (currently only for Native AOT).
            _serviceContainerFactory.AddServiceFactory<ICrashInfoService>((services) => CreateCrashInfoService(services, debuggerServices));
            OnFlushEvent.Register(() => FlushService<ICrashInfoService>());

            if (Host.HostType == HostType.DbgEng)
            {
                _serviceContainerFactory.AddServiceFactory<IMemoryRegionService>((services) => new MemoryRegionServiceFromDebuggerServices(debuggerServices));
            }

            Finished();

            TargetWrapper targetWrapper = Services.GetService<TargetWrapper>();
            targetWrapper?.ServiceWrapper.AddServiceWrapper(ClrmaServiceWrapper.IID_ICLRMAService, () => new ClrmaServiceWrapper(this, Services, targetWrapper.ServiceWrapper));
        }

        private static bool CreateCrashInfoServiceForModule(IModule module, out ICrashInfoService crashInfoService)
        {
            crashInfoService = null;
            if (module == null || module.IsManaged)
            {
                return false;
            }
            if ((module as IExportSymbols)?.TryGetSymbolAddress(CrashInfoService.DOTNET_RUNTIME_DEBUG_HEADER_NAME, out ulong headerAddr) != true)
            {
                Trace.TraceInformation($"CrashInfoService: {CrashInfoService.DOTNET_RUNTIME_DEBUG_HEADER_NAME} export not found in module {module.FileName}");
                return false;
            }
            IMemoryService memoryService = module.Services.GetService<IMemoryService>();
            if (!memoryService.Read<uint>(ref headerAddr, out uint headerValue) ||
                headerValue != CrashInfoService.DOTNET_RUNTIME_DEBUG_HEADER_COOKIE ||
                !memoryService.Read<ushort>(ref headerAddr, out ushort majorVersion) || majorVersion < 3 || // .NET 8 and later
                !memoryService.Read<ushort>(ref headerAddr, out ushort _))
            {
                Trace.TraceInformation($"CrashInfoService: .NET 8+ {CrashInfoService.DOTNET_RUNTIME_DEBUG_HEADER_NAME} not found in module {module.FileName}");
                return false;
            }

            if (!memoryService.Read<uint>(ref headerAddr, out uint flags) ||
                memoryService.PointerSize != (flags == 0x1 ? 8 : 4) ||
                !memoryService.Read<uint>(ref headerAddr, out uint _)) // padding
            {
                Trace.TraceError($"CrashInfoService: Failed to read DotNetRuntimeDebugHeader flags or padding in module {module.FileName}");
                return false;
            }

            headerAddr += (uint)memoryService.PointerSize; // skip DebugEntries array

            // read GlobalEntries array
            if (!memoryService.ReadPointer(ref headerAddr, out ulong globalEntryArrayAddress) || globalEntryArrayAddress == 0)
            {
                Trace.TraceError($"CrashInfoService: Unable to read GlobalEntry array");
                return false;
            }
            uint maxGlobalEntries = (majorVersion >= 4 /*.NET 9 or later*/) ? CrashInfoService.MAX_GLOBAL_ENTRIES_ARRAY_SIZE_NET9_PLUS : CrashInfoService.MAX_GLOBAL_ENTRIES_ARRAY_SIZE_NET8;
            for (int i = 0; i < maxGlobalEntries; i++)
            {
                if (!memoryService.ReadPointer(ref globalEntryArrayAddress, out ulong globalNameAddress) || globalNameAddress == 0 ||
                    !memoryService.ReadPointer(ref globalEntryArrayAddress, out ulong globalValueAddress) || globalValueAddress == 0 ||
                    !memoryService.ReadAnsiString(CrashInfoService.MAX_GLOBAL_ENTRY_NAME_CHARS, globalNameAddress, out string globalName))
                {
                    break; // no more global entries
                }

                if (!string.Equals(globalName, "g_CrashInfoBuffer", StringComparison.Ordinal))
                {
                    continue; // not the crash info buffer
                }

                ulong triageBufferAddress = globalValueAddress;
                Span<byte> buffer = new byte[CrashInfoService.MAX_CRASHINFOBUFFER_SIZE];
                if (memoryService.ReadMemory(triageBufferAddress, buffer, out int bytesRead) && bytesRead > 0 && bytesRead <= CrashInfoService.MAX_CRASHINFOBUFFER_SIZE)
                {
                    // truncate the buffer to the null terminated string in the buffer
                    int nullTerminatorIndex = buffer.IndexOf((byte)0);
                    if (nullTerminatorIndex >= 0)
                    {
                        buffer = buffer.Slice(0, nullTerminatorIndex);
                        if (buffer.Length > 0)
                        {
                            crashInfoService = CrashInfoService.Create(0, buffer, module.Services.GetService<IModuleService>());
                            return true;
                        }
                        else
                        {
                            Trace.TraceError($"CrashInfoService: g_CrashInfoBuffer is empty in module {module.FileName}");
                        }
                    }
                    else
                    {
                        Trace.TraceError($"CrashInfoService: g_CrashInfoBuffer is not null terminated in module {module.FileName}");
                    }
                }
                else
                {
                    Trace.TraceError($"CrashInfoService: ReadMemory({triageBufferAddress}) failed in module {module.FileName}");
                }
            }
            return false;
        }

        private unsafe ICrashInfoService CreateCrashInfoService(IServiceProvider services, DebuggerServices debuggerServices)
        {
            // For Linux/OSX dumps loaded under dbgeng the GetLastException API doesn't return the necessary information
            if (Host.HostType == HostType.DbgEng && (OperatingSystem == OSPlatform.Linux || OperatingSystem == OSPlatform.OSX))
            {
                return SpecialDiagInfo.CreateCrashInfoService(services);
            }
            HResult hr = debuggerServices.GetLastException(out uint processId, out int threadIndex, out EXCEPTION_RECORD64 exceptionRecord);
            if (hr.IsOK)
            {
                if (exceptionRecord.ExceptionCode == CrashInfoService.STATUS_STACK_BUFFER_OVERRUN &&
                    exceptionRecord.NumberParameters >= 4 &&
                    exceptionRecord.ExceptionInformation[0] == CrashInfoService.FAST_FAIL_EXCEPTION_DOTNET_AOT)
                {
                    uint hresult = (uint)exceptionRecord.ExceptionInformation[1];
                    ulong triageBufferAddress = exceptionRecord.ExceptionInformation[2];
                    int triageBufferSize = (int)exceptionRecord.ExceptionInformation[3];

                    Span<byte> buffer = new byte[triageBufferSize];
                    if (services.GetService<IMemoryService>().ReadMemory(triageBufferAddress, buffer, out int bytesRead) && bytesRead == triageBufferSize)
                    {
                        return CrashInfoService.Create(hresult, buffer, services.GetService<IModuleService>());
                    }
                    else
                    {
                        Trace.TraceError($"CrashInfoService: ReadMemory({triageBufferAddress}) failed");
                    }
                }
            }

            // if the above did not located the crash info service, then look for the DotNetRuntimeDebugHeader
            IModule entryPointModule = services.GetService<IModuleService>().EntryPointModule;
            if (entryPointModule == null)
            {
                Trace.TraceError("CrashInfoService: No entry point module found");
                return null;
            }
            if (CreateCrashInfoServiceForModule(entryPointModule, out ICrashInfoService crashInfoService))
            {
                return crashInfoService;
            }
            // if the entry point module did not have the crash info service, then look for a library with the same name as the entry point
            string fileName = entryPointModule.FileName;
            if (fileName == null)
            {
                Trace.TraceError("CrashInfoService: Entry point module has no file name");
                return null;
            }
            if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                fileName = fileName.Substring(0, fileName.Length - 4) + ".dll"; // look for a dll with the same name
                foreach (IModule speculativeAppModule in services.GetService<IModuleService>().GetModuleFromModuleName(fileName))
                {
                    if (CreateCrashInfoServiceForModule(speculativeAppModule, out crashInfoService))
                    {
                        return crashInfoService;
                    }
                }
            }

            return null;
        }
    }
}

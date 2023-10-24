// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Microsoft.Diagnostics.Runtime.Utilities;
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
        internal TargetFromDebuggerServices(DebuggerServices debuggerServices, IHost host, int id)
            : base(host, id, dumpPath: null)
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

            hr = debuggerServices.GetExecutingProcessorType(out IMAGE_FILE_MACHINE type);
            if (hr == HResult.S_OK)
            {
                Architecture = type switch
                {
                    IMAGE_FILE_MACHINE.I386 => Architecture.X86,
                    IMAGE_FILE_MACHINE.ARM => Architecture.Arm,
                    IMAGE_FILE_MACHINE.THUMB => Architecture.Arm,
                    IMAGE_FILE_MACHINE.THUMB2 => Architecture.Arm,
                    IMAGE_FILE_MACHINE.AMD64 => Architecture.X64,
                    IMAGE_FILE_MACHINE.ARM64 => Architecture.Arm64,
                    _ => throw new PlatformNotSupportedException($"Machine type not supported: {type}"),
                };
            }
            else
            {
                throw new PlatformNotSupportedException($"GetExecutingProcessorType() FAILED {hr:X8}");
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

            if (debuggerServices.DebugClient is not null)
            {
                _serviceContainerFactory.AddServiceFactory<IMemoryRegionService>((services) => new MemoryRegionServiceFromDebuggerServices(debuggerServices.DebugClient));
            }

            Finished();
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
                        return CrashInfoService.Create(hresult, buffer);
                    }
                    else
                    {
                        Trace.TraceError($"CrashInfoService: ReadMemory({triageBufferAddress}) failed");
                    }
                }
            }
            return null;
        }
    }
}

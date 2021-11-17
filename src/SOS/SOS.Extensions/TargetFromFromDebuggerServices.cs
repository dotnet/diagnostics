// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Microsoft.Diagnostics.Runtime.Interop;
using Microsoft.Diagnostics.Runtime.Utilities;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Architecture = System.Runtime.InteropServices.Architecture;

namespace SOS.Extensions
{
    /// <summary>
    /// ITarget implementation for the ClrMD IDataReader
    /// </summary>
    internal class TargetFromDebuggerServices : Target
    {
        /// <summary>
        /// Create a target instance from IDataReader
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
            if (qualifier >= DEBUG_CLASS_QUALIFIER.USER_WINDOWS_SMALL_DUMP) {
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
            IMemoryService rawMemoryService = new MemoryServiceFromDebuggerServices(this, debuggerServices);
            ServiceProvider.AddServiceFactory<IModuleService>(() => new ModuleServiceFromDebuggerServices(this, rawMemoryService, debuggerServices));
            ServiceProvider.AddServiceFactory<IThreadService>(() => new ThreadServiceFromDebuggerServices(this, debuggerServices));
            ServiceProvider.AddServiceFactory<IMemoryService>(() => {
                Debug.Assert(Host.HostType != HostType.DotnetDump);
                IMemoryService memoryService = rawMemoryService;
                if (IsDump && Host.HostType == HostType.Lldb)
                {
                    // This is a special memory service that maps the managed assemblies' metadata into the address 
                    // space. The lldb debugger returns zero's (instead of failing the memory read) for missing pages
                    // in core dumps that older (< 5.0) createdumps generate so it needs this special metadata mapping 
                    // memory service. dotnet-dump needs this logic for clrstack -i (uses ICorDebug data targets).
                    return new MetadataMappingMemoryService(this, memoryService);
                }
                return memoryService;
            });
        }
    }
}

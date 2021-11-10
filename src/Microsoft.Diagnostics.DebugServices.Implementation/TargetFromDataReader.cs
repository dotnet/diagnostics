// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.Runtime;
using System;
using System.Runtime.InteropServices;
using Architecture = System.Runtime.InteropServices.Architecture;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// ITarget implementation for the ClrMD IDataReader
    /// </summary>
    public class TargetFromDataReader : Target
    {
        private readonly IDataReader _dataReader;

        /// <summary>
        /// Create a target instance from IDataReader
        /// </summary>
        /// <param name="dataReader">IDataReader</param>
        /// <param name="targetOS">target operating system</param>
        /// <param name="host">the host instance</param>
        /// <param name="id">target id</param>
        /// <param name="dumpPath">path of dump for this target</param>
        public TargetFromDataReader(IDataReader dataReader, OSPlatform targetOS, IHost host, int id, string dumpPath)
            : base(host, id, dumpPath)
        {
            _dataReader = dataReader;

            OperatingSystem = targetOS;
            IsDump = true;
            OnFlushEvent.Register(dataReader.FlushCachedData);

            Architecture = dataReader.Architecture switch
            {
                Microsoft.Diagnostics.Runtime.Architecture.Amd64 => Architecture.X64,
                Microsoft.Diagnostics.Runtime.Architecture.X86 => Architecture.X86,
                Microsoft.Diagnostics.Runtime.Architecture.Arm => Architecture.Arm,
                Microsoft.Diagnostics.Runtime.Architecture.Arm64 => Architecture.Arm64,
                _ => throw new PlatformNotSupportedException($"{dataReader.Architecture}"),
            };

            if (dataReader.ProcessId != -1)
            {
                ProcessId = (uint)dataReader.ProcessId;
            }

            // Add the thread, memory, and module services
            IMemoryService rawMemoryService = new MemoryServiceFromDataReader(_dataReader);
            ServiceProvider.AddServiceFactory<IThreadService>(() => new ThreadServiceFromDataReader(this, _dataReader));
            ServiceProvider.AddServiceFactory<IModuleService>(() => new ModuleServiceFromDataReader(this, rawMemoryService, _dataReader));
            ServiceProvider.AddServiceFactory<IMemoryService>(() =>
            {
                IMemoryService memoryService = rawMemoryService;
                if (IsDump)
                {
                    memoryService = new ImageMappingMemoryService(this, memoryService);
                    // Any dump created for a MacOS target does not have managed assemblies in the module service so
                    // we need to use the metadata mapping memory service to make sure the metadata is available.
                    if (targetOS == OSPlatform.OSX)
                    {
                        memoryService = new MetadataMappingMemoryService(this, memoryService);
                    }
                }
                return memoryService;
            });
        }
    }
}

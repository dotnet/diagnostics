// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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

            if (dataReader.ProcessId != -1) {
                ProcessId = (uint)dataReader.ProcessId;
            }

            // Add the thread, memory, and module services
            ServiceProvider.AddServiceFactory<IThreadService>(() => new ThreadServiceFromDataReader(this, _dataReader));
            ServiceProvider.AddServiceFactory<IModuleService>(() => new ModuleServiceFromDataReader(this, _dataReader));
            ServiceProvider.AddServiceFactory<IMemoryService>(() => {
                IMemoryService memoryService = new MemoryServiceFromDataReader(_dataReader);
                if (IsDump && Host.HostType == HostType.DotnetDump)
                {
                    memoryService = new ImageMappingMemoryService(this, memoryService);
                }
                return memoryService;
            });
        }
    }
}

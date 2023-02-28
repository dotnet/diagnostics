// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime;
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
            Architecture = dataReader.Architecture;

            if (dataReader.ProcessId != -1)
            {
                ProcessId = (uint)dataReader.ProcessId;
            }

            OnFlushEvent.Register(dataReader.FlushCachedData);

            // Add the thread, memory, and module services
            _serviceContainerFactory.AddServiceFactory<IThreadService>((services) => new ThreadServiceFromDataReader(services, _dataReader));
            _serviceContainerFactory.AddServiceFactory<IModuleService>((services) => new ModuleServiceFromDataReader(services, _dataReader));
            _serviceContainerFactory.AddServiceFactory<IMemoryService>((_) => {
                IMemoryService memoryService = new MemoryServiceFromDataReader(_dataReader);
                if (IsDump)
                {
                    // The target container factory needs to be cloned for the memory services so the original IMemoryService
                    // factory can be removed so it doesn't inadvertently get called. The image mapping service is going to
                    // replace it with the memory service instance passed. The clone.Build() creates a new separate service
                    // container instance for the image mapping service.
                    ServiceContainerFactory clone = _serviceContainerFactory.Clone();
                    clone.RemoveServiceFactory<IMemoryService>();

                    // The underlying host (dotnet-dump usually) doesn't map native modules into the address space
                    memoryService = new ImageMappingMemoryService(clone.Build(), memoryService, managed: false);

                    // Any dump created for a MacOS target does not have managed assemblies in the native module service so
                    // we need to use this managed mapping memory service to make sure the metadata is available and 7.0 Linux
                    // builds have an extra System.Private.CoreLib module mapping that causes the native image mapper not to
                    // be able to map in the metadata.
                    if (targetOS == OSPlatform.OSX || targetOS == OSPlatform.Linux)
                    {
                        memoryService = new ImageMappingMemoryService(clone.Build(), memoryService, managed: true);
                    }
                }
                return memoryService;
            });

            Finished();
        }
    }
}

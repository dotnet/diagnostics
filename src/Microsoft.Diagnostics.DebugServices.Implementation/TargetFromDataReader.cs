// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.DataReaders.Implementation;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// ITarget implementation for the ClrMD IDataReader
    /// </summary>
    public class TargetFromDataReader : Target
    {
        private readonly DataTarget _dataTarget;

        /// <summary>
        /// Create a target instance from IDataReader
        /// </summary>
        /// <param name="dataTarget">data target from clrmd</param>
        /// <param name="targetOS">target operating system</param>
        /// <param name="host">the host instance</param>
        /// <param name="dumpPath">path of dump for this target</param>
        /// <exception cref="DiagnosticsException">can not construct target instance</exception>
        public TargetFromDataReader(DataTarget dataTarget, OSPlatform targetOS, IHost host, string dumpPath)
            : base(host, dumpPath)
        {
            _dataTarget = dataTarget;
            IDataReader dataReader = dataTarget.DataReader;

            OperatingSystem = targetOS;
            IsDump = true;
            Architecture = dataReader.Architecture;

            if (dataReader.ProcessId != -1)
            {
                ProcessId = (uint)dataReader.ProcessId;
            }

            OnFlushEvent.Register(dataReader.FlushCachedData);

            if (dataReader is not IThreadReader)
            {
                throw new DiagnosticsException("The required IThreadReader is not implemented on data target");
            }

            // Add the thread, memory, and module services
            _serviceContainerFactory.AddServiceFactory<IThreadService>((services) => new ThreadServiceFromDataReader(services, dataReader));
            _serviceContainerFactory.AddServiceFactory<IModuleService>((services) => new ModuleServiceFromDataReader(services, dataReader));
            _serviceContainerFactory.AddServiceFactory<IMemoryService>((_) => {
                IMemoryService memoryService = new MemoryServiceFromDataReader(dataReader);
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

            // Add optional crash info service (currently only for Native AOT on Linux/MacOS).
            _serviceContainerFactory.AddServiceFactory<ICrashInfoService>((services) => SpecialDiagInfo.CreateCrashInfoService(services));
            OnFlushEvent.Register(() => FlushService<ICrashInfoService>());

            Finished();
        }

        public override void Destroy()
        {
            base.Destroy();
            _dataTarget.Dispose();
        }
    }
}

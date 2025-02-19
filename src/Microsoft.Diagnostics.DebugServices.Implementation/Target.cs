// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.FileFormats;
using Architecture = System.Runtime.InteropServices.Architecture;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// ITarget base implementation
    /// </summary>
    public abstract class Target : ITarget
    {
        private readonly string _dumpPath;
        private ServiceContainer _serviceContainer;

        protected readonly ServiceContainerFactory _serviceContainerFactory;

        public Target(IHost host, string dumpPath)
        {
            Host = host;
            _dumpPath = dumpPath;

            OnFlushEvent = new ServiceEvent();
            OnDestroyEvent = new ServiceEvent();

            // Initialize the per-target services.
            _serviceContainerFactory = host.Services.GetService<IServiceManager>().CreateServiceContainerFactory(ServiceScope.Target, host.Services);
            _serviceContainerFactory.AddServiceFactory<ITarget>((_) => this);
            _serviceContainerFactory.AddServiceFactory<Reader>(CreateReader);
        }

        protected void Finished()
        {
            // Add the target to the host
            Id = Host.AddTarget(this);

            // Now the that the target is completely initialized, finalize container and fire event
            _serviceContainer = _serviceContainerFactory.Build();
            Host.OnTargetCreate.Fire(this);

            Trace.TraceInformation($"Created target #{Id} {_dumpPath}");
        }

        protected void FlushService<T>() => _serviceContainer?.RemoveService(typeof(T));

        #region ITarget

        /// <summary>
        /// Returns the host interface instance
        /// </summary>
        public IHost Host { get; }

        /// <summary>
        /// The target id
        /// </summary>
        public int Id { get; private set; }

        /// <summary>
        /// Returns the target OS (which may be different from the OS this is running on)
        /// </summary>
        public OSPlatform OperatingSystem { get; protected set; }

        /// <summary>
        /// The target architecture/processor
        /// </summary>
        public Architecture Architecture { get; protected set; }

        /// <summary>
        /// Returns true if dump, false if live session or snapshot
        /// </summary>
        public bool IsDump { get; protected set; }

        /// <summary>
        /// The target's process id or null no process
        /// </summary>
        public uint? ProcessId { get; protected set; }

        /// <summary>
        /// The per target services.
        /// </summary>
        public IServiceProvider Services => _serviceContainer;

        /// <summary>
        /// Invoked when this target is flushed (via the Flush() call).
        /// </summary>
        public IServiceEvent OnFlushEvent { get; }

        /// <summary>
        /// Flushes any cached state in the target.
        /// </summary>
        public void Flush()
        {
            Trace.TraceInformation($"Flushing target #{Id}");
            OnFlushEvent.Fire();
        }

        /// <summary>
        /// Invoked when the target is destroyed
        /// </summary>
        public IServiceEvent OnDestroyEvent { get; }

        /// <summary>
        /// Cleans up the target and releases target's resources.
        /// </summary>
        public virtual void Destroy()
        {
            Trace.TraceInformation($"Destroy target #{Id}");
            OnDestroyEvent.Fire();
            _serviceContainer.RemoveService(typeof(ITarget));
            _serviceContainer.DisposeServices();
        }

        #endregion

        /// <summary>
        /// Create the file format reader used to read and layout TStruct derived structures from memory
        /// </summary>
        private static Reader CreateReader(IServiceProvider services)
        {
            IMemoryService memoryService = services.GetService<IMemoryService>();
            Stream stream = memoryService.CreateMemoryStream();
            LayoutManager layoutManager = new LayoutManager()
                                            .AddPrimitives()
                                            .AddEnumTypes()
                                            .AddSizeT(memoryService.PointerSize)
                                            .AddPointerTypes()
                                            .AddNullTerminatedString()
                                            .AddTStructTypes();
            return new Reader(new StreamAddressSpace(stream), layoutManager);
        }

        public override bool Equals(object obj)
        {
            return Id == ((ITarget)obj).Id;
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        public override string ToString()
        {
            StringBuilder sb = new();
            string process = ProcessId.HasValue ? string.Format("{0} (0x{0:X})", ProcessId.Value) : "<none>";
            sb.Append($"Target OS: {OperatingSystem} Architecture: {Architecture} ProcessId: {process}");
            if (_dumpPath != null)
            {
                sb.Append($" {_dumpPath}");
            }
            return sb.ToString();
        }
    }
}

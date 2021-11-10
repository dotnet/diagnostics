// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Architecture = System.Runtime.InteropServices.Architecture;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    /// <summary>
    /// ITarget base implementation
    /// </summary>
    public abstract class Target : ITarget, IDisposable
    {
        private readonly string _dumpPath;
        private string _tempDirectory;

        public readonly ServiceProvider ServiceProvider;

        public Target(IHost host, int id, string dumpPath)
        {
            Trace.TraceInformation($"Creating target #{Id}");
            Host = host;
            Id = id;
            _dumpPath = dumpPath;

            OnFlushEvent = new ServiceEvent();
            OnDestroyEvent = new ServiceEvent();

            // Initialize the per-target services
            ServiceProvider = new ServiceProvider(host.Services);

            // Add the per-target services
            ServiceProvider.AddService<ITarget>(this);
            ServiceProvider.AddServiceFactory<IRuntimeService>(() => new RuntimeService(this));
        }

        #region ITarget

        /// <summary>
        /// Returns the host interface instance
        /// </summary>
        public IHost Host { get; }

        /// <summary>
        /// The target id
        /// </summary>
        public int Id { get; }

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
        /// Returns the unique temporary directory for this instance of SOS
        /// </summary>
        public string GetTempDirectory()
        {
            if (_tempDirectory == null)
            {
                // Use the SOS process's id if can't get the target's
                uint processId = ProcessId.GetValueOrDefault((uint)Process.GetCurrentProcess().Id);

                // SOS depends on that the temp directory ends with "/".
                _tempDirectory = Path.Combine(Path.GetTempPath(), "sos" + processId.ToString()) + Path.DirectorySeparatorChar;
                Directory.CreateDirectory(_tempDirectory);
            }
            return _tempDirectory;
        }

        /// <summary>
        /// The per target services.
        /// </summary>
        public IServiceProvider Services => ServiceProvider;

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
        /// Invoked when the target is closed.
        /// </summary>
        public IServiceEvent OnDestroyEvent { get; }

        #endregion

        /// <summary>
        /// Releases the target and the target's resources.
        /// </summary>
        public void Dispose()
        {
            Trace.TraceInformation($"Disposing target #{Id}");
            OnDestroyEvent.Fire();
            CleanupTempDirectory();
        }

        private void CleanupTempDirectory()
        {
            if (_tempDirectory != null)
            {
                try
                {
                    foreach (string file in Directory.EnumerateFiles(_tempDirectory))
                    {
                        File.Delete(file);
                    }
                    Directory.Delete(_tempDirectory);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                }
                _tempDirectory = null;
            }
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
            var sb = new StringBuilder();
            string process = ProcessId.HasValue ? string.Format("{0} (0x{0:X})", ProcessId.Value) : "<none>";
            sb.AppendLine($"Target OS: {OperatingSystem} Architecture: {Architecture} ProcessId: {process}");
            if (_tempDirectory != null)
            {
                sb.AppendLine($"Temp path: {_tempDirectory}");
            }
            if (_dumpPath != null)
            {
                sb.AppendLine($"Dump path: {_dumpPath}");
            }
            var runtimeService = ServiceProvider.GetService<IRuntimeService>();
            if (runtimeService != null)
            {
                sb.AppendLine(runtimeService.ToString());
            }
            return sb.ToString();
        }
    }
}

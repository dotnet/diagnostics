// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// The runtime type.
    /// </summary>
    public enum RuntimeType
    {
        Desktop = 0,
        NetCore = 1,
        SingleFile = 2,
        Unknown = 3
    }

    /// <summary>
    /// Provides runtime info and instance
    /// </summary>
    public interface IRuntime
    {
        /// <summary>
        /// Runtime id
        /// </summary>
        int Id { get; }

        /// <summary>
        /// The target for this runtime.
        /// </summary>
        ITarget Target { get; }

        /// <summary>
        /// The per target services like clrmd's ClrInfo and ClrRuntime.
        /// </summary>
        IServiceProvider Services { get; }

        /// <summary>
        /// Returns the runtime OS and type
        /// </summary>
        RuntimeType RuntimeType { get; }

        /// <summary>
        /// Returns the runtime module
        /// </summary>
        IModule RuntimeModule { get; }

        /// <summary>
        /// Directory of the runtime module (coreclr.dll, libcoreclr.so, etc.)
        /// </summary>
        string RuntimeModuleDirectory { get; set; }

        /// <summary>
        /// Returns the DAC file path
        /// </summary>
        string GetDacFilePath();

        /// <summary>
        /// Returns the DBI file path
        /// </summary>
        string GetDbiFilePath();
    }
}

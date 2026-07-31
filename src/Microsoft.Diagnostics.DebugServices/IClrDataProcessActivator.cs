// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Activates a data-access interface (IXCLRDataProcess) for a runtime through dbgshim instead
    /// of having ClrMD locate and load the DAC/cDAC itself. The SOS hosting layer supplies the
    /// implementation: it builds a runtime-bound data target, hands the runtime module base to
    /// dbgshim, which prefers the co-located cDAC and falls back to the legacy DAC via the library
    /// provider. The resulting interface is then handed to ClrMD through
    /// <c>DataTarget.AddLoadedRuntime</c>.
    ///
    /// This service is optional. When it is not registered, or when activation is not viable for a
    /// given runtime (for example, an older target for which the cDAC declines), the caller keeps
    /// using the existing ClrMD load path, so behavior is unchanged for those targets.
    /// </summary>
    public interface IClrDataProcessActivator
    {
        /// <summary>
        /// Attempts to activate an exclusive IXCLRDataProcess for <paramref name="runtime"/> through
        /// dbgshim. Ownership of the returned interface transfers to the caller, which must release
        /// the single reference when the runtime is disposed (this matches the ownership contract of
        /// <c>DataTarget.AddLoadedRuntime</c>).
        /// </summary>
        /// <param name="runtime">The runtime to activate data access for.</param>
        /// <returns>
        /// An AddRef'd IUnknown for the runtime's IXCLRDataProcess, or <see cref="IntPtr.Zero"/> when
        /// activation is not available or the target is not serviceable through this path (the caller
        /// should then fall back to loading the DAC itself).
        /// </returns>
        IntPtr CreateClrDataProcess(IRuntime runtime);
    }
}

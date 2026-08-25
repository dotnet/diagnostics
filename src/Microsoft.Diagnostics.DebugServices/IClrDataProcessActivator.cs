// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Owns an IXCLRDataProcess and its activation resources.
    /// </summary>
    public interface IClrDataProcess : IDisposable
    {
        /// <summary>
        /// Gets the IXCLRDataProcess interface pointer.
        /// </summary>
        IntPtr Interface { get; }
    }

    /// <summary>
    /// Creates CLR data-access interfaces for runtimes.
    /// </summary>
    public interface IClrDataProcessActivator
    {
        /// <summary>
        /// Creates an IXCLRDataProcess for <paramref name="runtime"/>.
        /// </summary>
        /// <param name="runtime">The runtime to activate data access for.</param>
        /// <param name="clrDataProcess">The owned IXCLRDataProcess instance, if created.</param>
        /// <returns>The activation HRESULT.</returns>
        int CreateClrDataProcessFromCDac(IRuntime runtime, out IClrDataProcess clrDataProcess);
    }
}

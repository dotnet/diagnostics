// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Creates CLR data-access interfaces for runtimes.
    /// </summary>
    public interface IClrDataProcessActivator
    {
        /// <summary>
        /// Creates an IXCLRDataProcess for <paramref name="runtime"/>.
        /// </summary>
        /// <param name="runtime">The runtime to activate data access for.</param>
        /// <param name="policy">The cDAC-versus-DAC activation policy.</param>
        /// <returns>
        /// An owned IXCLRDataProcess reference, or <see cref="IntPtr.Zero"/> if one could not be
        /// created.
        /// </returns>
        IntPtr CreateClrDataProcess(IRuntime runtime, CDacLoadPolicy policy);
    }
}

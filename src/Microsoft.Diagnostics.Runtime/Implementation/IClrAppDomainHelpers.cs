// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.Implementation
{
    internal interface IClrAppDomainHelpers
    {
        string? GetConfigFile(ClrAppDomain domain);
        string? GetApplicationBase(ClrAppDomain domain);
        ulong GetLoaderAllocator(ClrAppDomain domain);
        IClrNativeHeapHelpers GetNativeHeapHelpers();
    }
}
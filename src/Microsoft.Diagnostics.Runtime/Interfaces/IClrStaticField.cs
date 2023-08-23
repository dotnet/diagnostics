// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.Interfaces
{
    public interface IClrStaticField : IClrField
    {
        ulong GetAddress(IClrAppDomain appDomain);
        bool IsInitialized(IClrAppDomain appDomain);
        T Read<T>(IClrAppDomain appDomain) where T : unmanaged;
        IClrValue ReadObject(IClrAppDomain appDomain);
        string? ReadString(IClrAppDomain appDomain);
        IClrValue ReadStruct(IClrAppDomain appDomain);
    }
}

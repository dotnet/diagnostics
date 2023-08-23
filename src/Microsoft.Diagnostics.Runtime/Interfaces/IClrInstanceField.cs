// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.Interfaces
{
    public interface IClrInstanceField : IClrField
    {
        ulong GetAddress(ulong objRef);
        ulong GetAddress(ulong objRef, bool interior);
        T Read<T>(ulong objRef, bool interior) where T : unmanaged;
        IClrValue ReadObject(ulong objRef, bool interior);
        string? ReadString(ulong objRef, bool interior);
        IClrValue ReadStruct(ulong objRef, bool interior);
    }
}

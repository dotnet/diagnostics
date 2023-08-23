// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;

namespace Microsoft.Diagnostics.Runtime.Implementation
{
    internal interface IClrMethodHelpers
    {
        IDataReader DataReader { get; }

        bool GetSignature(ulong methodDesc, out string? signature);
        ImmutableArray<ILToNativeMap> GetILMap(ClrMethod method);
        ulong GetILForModule(ulong address, uint rva);
    }
}

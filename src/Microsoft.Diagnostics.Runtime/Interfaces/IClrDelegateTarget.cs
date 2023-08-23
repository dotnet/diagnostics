// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.Interfaces
{
    public interface IClrDelegateTarget
    {
        IClrMethod Method { get; }
        IClrDelegate Parent { get; }
        IClrValue TargetObject { get; }
    }
}
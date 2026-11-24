// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.DataContractReader.Contracts
{
    // The linked DAC declarations need the 32-bit ABI type, but SOS does not
    // use IRuntimeTypeSystem or the rest of the cDAC contract implementation.
    public enum CorElementType : int
    {
        End = 0,
    }
}

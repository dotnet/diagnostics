// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// The binary under which the files is archived.
    /// </summary>
    public enum SymbolProperties
    {
        /// <summary>
        /// The binary is archived under its own properties.
        /// </summary>
        Self,

        /// <summary>
        /// The binary is archived under coreclr's properties.
        /// </summary>
        Coreclr
    }
}

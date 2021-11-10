// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Module export symbol lookup
    /// </summary>
    public interface IExportSymbols
    {
        /// <summary>
        /// Returns the address of a module export symbol if found
        /// </summary>
        /// <param name="name">symbol name (without the module name prepended)</param>
        /// <param name="offset">address returned</param>
        /// <returns>true if found</returns>
        bool TryGetSymbolAddress(string name, out ulong offset);
    }
}

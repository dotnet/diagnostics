// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.


namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Module symbol lookup
    /// </summary>
    public interface IModuleSymbols
    {
        /// <summary>
        /// Returns the symbol name and displacement if found
        /// </summary>
        /// <param name="address">address of symbol to find</param>
        /// <param name="symbol">symbol name (without the module name prepended)</param>
        /// <param name="displacement">offset from symbol</param>
        /// <returns>true if found</returns>
        bool TryGetSymbolName(ulong address, out string symbol, out ulong displacement);

        /// <summary>
        /// Returns the address of a module symbol if found
        /// </summary>
        /// <param name="name">symbol name (without the module name prepended)</param>
        /// <param name="address">address of symbol</param>
        /// <returns>true if found</returns>
        bool TryGetSymbolAddress(string name, out ulong address);

        /// <summary>
        /// Searches for a type by name
        /// </summary>
        /// <param name="typeName">type name to find</param>
        /// <param name="type">returned type if found</param>
        /// <returns>true if type found</returns>
        bool TryGetType(string typeName, out IType type);
    }
}

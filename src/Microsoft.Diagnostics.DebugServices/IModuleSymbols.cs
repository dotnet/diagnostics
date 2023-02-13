// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.


namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// The status of the symbols for a module.
    /// </summary>
    public enum SymbolStatus
    {
        /// <summary>
        /// The status of the symbols is unknown.  The symbol may be
        /// loaded or unloaded.
        /// </summary>
        Unknown,
        
        /// <summary>
        /// The debugger has successfully loaded symbols for this module.
        /// </summary>
        Loaded,

        /// <summary>
        /// The debugger does not have symbols loaded for this module.
        /// </summary>
        NotLoaded,

        /// <summary>
        /// The debugger does not have symbols loaded for this module, but
        /// it is able to report addresses of exported functions.
        /// </summary>
        ExportOnly,
    }

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

        /// <summary>
        /// Returns the status of the symbols for this module.  This function may cause
        /// the debugger to load symbols for this module, which may take a long time.
        /// </summary>
        /// <returns>The status of symbols for this module.</returns>
        SymbolStatus GetSymbolStatus();
    }
}

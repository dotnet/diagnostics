// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    /// <summary>
    /// Describes a symbol within a module.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DEBUG_MODULE_AND_ID
    {
        /// <summary>
        /// The location in the target's virtual address space of the module's base address.
        /// </summary>
        public ulong ModuleBase;

        /// <summary>
        /// The symbol ID of the symbol within the module.
        /// </summary>
        public ulong Id;
    }
}
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// A method's mapping from IL to native offsets.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1823:Avoid unused private fields", Justification = "Native Definition")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Native Definition")]
    public struct ILToNativeMap
    {
        /// <summary>
        /// The IL offset for this entry.
        /// </summary>
        public int ILOffset;

        /// <summary>
        /// The native start offset of this IL entry.
        /// </summary>
        public ulong StartAddress;

        /// <summary>
        /// The native end offset of this IL entry.
        /// </summary>
        public ulong EndAddress;

        /// <summary>
        /// To string.
        /// </summary>
        /// <returns>A visual display of the map entry.</returns>
        public override string ToString()
        {
            return $"{ILOffset,2:X} - [{StartAddress:X}-{EndAddress:X}]";
        }

        /// <summary>
        /// Reserved.
        /// </summary>
        private readonly int _reserved;
    }
}

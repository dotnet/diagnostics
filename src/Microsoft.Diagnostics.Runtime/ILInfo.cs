// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// Returns information about the IL for a method.
    /// </summary>
    public sealed class ILInfo
    {
        /// <summary>
        /// Gets the address in memory of where the IL for a particular method is located.
        /// </summary>
        public ulong Address { get; }

        /// <summary>
        /// Gets the length (in bytes) of the IL method body.
        /// </summary>
        public int Length { get; }

        /// <summary>
        /// Gets the flags associated with the IL code.
        /// </summary>
        public uint Flags { get; }

        /// <summary>
        /// Gets the local variable signature token for this IL method.
        /// </summary>
        public uint LocalVarSignatureToken { get; }

        internal ILInfo(ulong address, int len, uint flags, uint localVarSignatureToken)
        {
            Address = address;
            Length = len;
            Flags = flags;
            LocalVarSignatureToken = localVarSignatureToken;
        }
    }
}
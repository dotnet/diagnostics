// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.Diagnostics.DebugServices
{
	/// <summary>
	/// Details about a register
	/// </summary>
    public struct RegisterInfo
    {
        public readonly int RegisterIndex;
        public readonly int RegisterOffset;
        public readonly int RegisterSize;
        public readonly string RegisterName;

        public RegisterInfo(int registerIndex, int registerOffset, int registerSize, string registerName)
        {
            RegisterIndex = registerIndex;
            RegisterOffset = registerOffset;
            RegisterSize = registerSize;
            RegisterName = registerName;
        }
    }
}

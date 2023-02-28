// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Describes a field in a type (IType)
    /// </summary>
    public interface IField
    {
        /// <summary>
        /// The type this field belongs
        /// </summary>
        IType Type { get; }

        /// <summary>
        /// The name of the field
        /// </summary>
        string Name { get; }

        /// <summary>
        /// The offset from the beginning of the instance
        /// </summary>
        uint Offset { get; }
    }
}

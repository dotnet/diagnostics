// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.FileFormats
{
    /// <summary>
    /// Information about a type that allows it to be parsed from a byte sequence either on its own
    /// or as a sub-component of another type
    /// </summary>
    public interface ILayout
    {
        /// <summary>
        /// The type being layed out
        /// </summary>
        Type Type { get; }

        /// <summary>
        /// Size in bytes for the serialized representation of the type.
        /// </summary>
        /// <throws>InvalidOperationException if IsFixedSize == false</throws>
        uint Size { get; }

        /// <summary>
        /// Returns true if all instances of the type serialize to the same number of bytes
        /// </summary>
        bool IsFixedSize { get; }

        /// <summary>
        /// The preferred alignment of this type
        /// </summary>
        uint NaturalAlignment { get; }

        /// <summary>
        /// The set of fields that compose the type
        /// </summary>
        IEnumerable<IField> Fields { get; }

        /// <summary>
        /// Size in bytes for the serialized representation of all the fields in this type.
        /// This may be less than Size because it does not account for trailing padding bytes
        /// after the last field.
        /// </summary>
        /// <throws>InvalidOperationException if IsFixedSize == false</throws>
        uint SizeAsBaseType { get; }

        /// <summary>
        /// Parse an instance from the dataSource starting at position
        /// </summary>
        object Read(IAddressSpace dataSource, ulong position);

        /// <summary>
        /// Parse an instance from the dataSource starting at position and report the number of bytes
        /// that were read
        /// </summary>
        object Read(IAddressSpace dataSource, ulong position, out uint bytesRead);
    }
}

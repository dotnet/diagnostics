// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;

namespace Microsoft.Diagnostics.Runtime
{
    /// <summary>
    /// Returns information about generic parameters.
    /// </summary>
    public readonly struct ClrGenericParameter
    {
        /// <summary>
        /// The metadata token of the parameter.
        /// </summary>
        public int MetadataToken { get; }

        /// <summary>
        /// The index of the parameter.
        /// </summary>
        public int Index { get; }

        /// <summary>
        /// The attributes of the parameter.
        /// </summary>
        public GenericParameterAttributes Attributes { get; }

        /// <summary>
        /// The name of the parameter.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Constructor.
        /// </summary>
        public ClrGenericParameter(int metadataToken, int index, GenericParameterAttributes attributes, string name)
        {
            MetadataToken = metadataToken;
            Index = index;
            Attributes = attributes;
            Name = name;
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Describes a native type in a module
    /// </summary>
    public interface IType
    {
        /// <summary>
        /// Name of the type
        /// </summary>
        string Name { get; }

        /// <summary>
        /// The module of the type 
        /// </summary>
        IModule Module { get; }

        /// <summary>
        /// A list of all the fields in the type
        /// </summary>
        List<IField> Fields { get; }

        /// <summary>
        /// Get a field by name
        /// </summary>
        /// <param name="fieldName">name of the field to find</param>
        /// <param name="field">the returned field if found</param>
        /// <returns>true if found</returns>
        bool TryGetField(string fieldName, out IField field);
    }
}

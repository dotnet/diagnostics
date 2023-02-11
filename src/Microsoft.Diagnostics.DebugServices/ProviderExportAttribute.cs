// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Marks classes or methods (provider factories) as providers (extensions to services).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class ProviderExportAttribute : Attribute
    {
        /// <summary>
        /// The interface or type to register the provider. If null, the provider type registered will be 
        /// he class itself or the return type of the method.
        /// </summary>
        public Type Type { get; set; }

        /// <summary>
        /// Default constructor.
        /// </summary>
        public ProviderExportAttribute()
        {
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Marks properties or methods to import another service when a service is instantiated.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public class ServiceImportAttribute : Attribute
    {
        /// <summary>
        /// If true, the service is optional and can even up null.
        /// </summary>
        public bool Optional { get; set; }

        /// <summary>
        /// Default constructor.
        /// </summary>
        public ServiceImportAttribute()
        {
        }
    }
}

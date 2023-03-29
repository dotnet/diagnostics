// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.DebugServices
{
    /// <summary>
    /// Marks properties or methods to import another service when a service is instantiated.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
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

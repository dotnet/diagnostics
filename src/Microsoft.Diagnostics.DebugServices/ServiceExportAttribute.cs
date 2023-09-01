// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.DebugServices
{
    public enum ServiceScope
    {
        Global,
        Context,
        Target,
        Module,
        Thread,
        Runtime,
        Max
    }

    /// <summary>
    /// Marks classes or methods (service factories) as services
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class ServiceExportAttribute : Attribute
    {
        /// <summary>
        /// The interface or type to register the service. If null, the service type registered will be
        /// the class itself or the return type of the method.
        /// </summary>
        public Type Type { get; set; }

        /// <summary>
        /// The scope of the service (global, per-target, per-context, per-runtime, etc).
        /// </summary>
        public ServiceScope Scope { get; set; }

        /// <summary>
        /// Default constructor.
        /// </summary>
        public ServiceExportAttribute()
        {
        }
    }
}

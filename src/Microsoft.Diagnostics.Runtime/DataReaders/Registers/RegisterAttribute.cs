// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Runtime
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class RegisterAttribute : Attribute
    {
        /// <summary>
        /// Gets or sets optional name override
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Gets register type and flags
        /// </summary>
        public RegisterType RegisterType { get; }

        public RegisterAttribute(RegisterType registerType)
        {
            RegisterType = registerType;
        }
    }
}
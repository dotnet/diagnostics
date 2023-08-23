// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime
{
    public enum StringCaching
    {
        /// <summary>
        /// Do not cache the value at all.  This will result in drastically lower memory
        /// usage at the cost of performance.
        /// </summary>
        None,

        /// <summary>
        /// Strings will be cached by the objects which hold them.  This will make repeated
        /// requests to get the same value MUCH faster, but at the cost of holding on to
        /// extra memory.
        /// </summary>
        Cache,

        /// <summary>
        /// Strings will be cached by the objects which hold them and they will also be
        /// interned, ensuring that the same string value will not be kept alive by multiple
        /// objects.  The danger here is that interned strings are never freed until the
        /// AppDomain they live in is unloaded (or never for .NET Core).  Field names will
        /// benefit from interning if you read a lot of fields for a lot of types.  It's
        /// unlikely that method names or type names will benefit from interning unless
        /// the same types are loaded into multiple AppDomains in the target process.
        /// </summary>
        Intern,
    }
}
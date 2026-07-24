// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.DebugServices
{
    public enum OutputType
    {
        Default = 0,
        Logging = 1,    // Used when logging to console is enabled. Allows the command output capture to ignore SOS logging output.
        Dml = 2,        // throws NotSupportedException if DML isn't supported or enabled.
    };
}

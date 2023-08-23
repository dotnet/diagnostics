// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.Diagnostics.Runtime.Implementation
{
    internal interface IClrThreadHelpers
    {
        IDataReader DataReader { get; }
        IEnumerable<ClrStackRoot> EnumerateStackRoots(ClrThread thread);
        IEnumerable<ClrStackFrame> EnumerateStackTrace(ClrThread thread, bool includeContext);
    }
}
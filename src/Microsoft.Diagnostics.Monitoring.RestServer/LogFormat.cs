// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.RestServer
{
    public enum LogFormat
    {
        None = 0,
        Json = 1,
        EventStream = 2
    }
}

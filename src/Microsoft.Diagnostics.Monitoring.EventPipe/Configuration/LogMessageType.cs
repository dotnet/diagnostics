// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    [Flags]
    public enum LogMessageType
    {
        Message = 0x2,
        FormattedMessage = 0x4,
        JsonMessage = 0x8
    }
}

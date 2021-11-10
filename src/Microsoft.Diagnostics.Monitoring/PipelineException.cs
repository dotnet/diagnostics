﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.Diagnostics.Monitoring
{
    internal class PipelineException : MonitoringException
    {
        public PipelineException(string message) : base(message) { }
        public PipelineException(string message, Exception inner) : base(message, inner) { }
    }
}

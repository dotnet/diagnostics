// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using System;

namespace Microsoft.Diagnostics.Monitoring.EventPipe
{
    internal interface ILogRecordLogger
    {
        void Log(
            in LogRecord log,
            ReadOnlySpan<KeyValuePair<string, object?>> attributes,
            in LogRecordScopeContainer scopes);

        Task PipelineStarted(CancellationToken token);
        Task PipelineStopped(CancellationToken token);
    }
}

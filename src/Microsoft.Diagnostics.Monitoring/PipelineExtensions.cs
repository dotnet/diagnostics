// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring
{
    internal static class PipelineExtensions
    {
        public static async Task StopAsync(this Pipeline pipeline, TimeSpan timeout)
        {
            using CancellationTokenSource cts = new CancellationTokenSource();
            cts.CancelAfter(timeout);
            await pipeline.StopAsync(cts.Token);
        }
    }
}

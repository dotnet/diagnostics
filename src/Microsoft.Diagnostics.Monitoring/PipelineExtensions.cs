// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring
{
    internal static class PipelineExtensions
    {
        public static Task StopAsync(this Pipeline pipeline, TimeSpan timeout)
        {
            CancellationTokenSource cts = new CancellationTokenSource();
            cts.CancelAfter(timeout);
            return pipeline.StopAsync(cts.Token);
        }
    }
}

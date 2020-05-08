// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.RestServer
{
    /// <summary>
    /// Same as FileStreamResult, but also cleans up the underlying stream provider once it's finished
    /// </summary>
    internal sealed class StreamWithCleanupResult : FileStreamResult
    {
        private readonly IStreamWithCleanup _streamResult;
        public StreamWithCleanupResult(IStreamWithCleanup streamResult, string contentType, string fileDownloadName) : base(streamResult.Stream, contentType)
        {
            FileDownloadName = fileDownloadName;
            _streamResult = streamResult;
        }

        public override async Task ExecuteResultAsync(ActionContext context)
        {
            try
            {
                await base.ExecuteResultAsync(context);
            }
            finally
            {
                await _streamResult.DisposeAsync();
            }
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.RestServer
{
    internal sealed class OutputStreamResult : ActionResult
    {
        private readonly Func<Stream, CancellationToken, Task> _action;
        private readonly string _contentType;
        private readonly string _fileDownloadName;
        private readonly KeyValueLogScope _scope;

        public OutputStreamResult(Func<Stream, CancellationToken, Task> action, string contentType, string fileDownloadName, KeyValueLogScope scope)
        {
            _contentType = contentType;
            _fileDownloadName = fileDownloadName;
            _action = action;
            _scope = scope;
        }

        public override async Task ExecuteResultAsync(ActionContext context)
        {
            ILogger<OutputStreamResult> logger = context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger<OutputStreamResult>();

            using var _ = logger.BeginScope(_scope);

            await context.InvokeAsync(async (token) =>
            {
                if (_fileDownloadName != null)
                {
                    ContentDispositionHeaderValue contentDispositionHeaderValue = new ContentDispositionHeaderValue("attachment");
                    contentDispositionHeaderValue.FileName = _fileDownloadName;
                    context.HttpContext.Response.Headers["Content-Disposition"] = contentDispositionHeaderValue.ToString();
                }
                context.HttpContext.Response.Headers["Content-Type"] = _contentType;

#if !NETSTANDARD2_0
                context.HttpContext.Features.Get<AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();
#else
                context.HttpContext.Features.Get<AspNetCore.Http.Features.IHttpBufferingFeature>()?.DisableResponseBuffering();
#endif

                await _action(context.HttpContext.Response.Body, token);

                logger.LogInformation("Written to HTTP stream.");
            }, logger);
        }
    }
}

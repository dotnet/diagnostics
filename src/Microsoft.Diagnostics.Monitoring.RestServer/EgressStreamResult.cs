// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.RestServer
{
    internal class EgressStreamResult : ActionResult
    {
        private readonly Func<IEgressService, CancellationToken, Task<EgressResult>> _egress;

        public EgressStreamResult(Func<CancellationToken, Task<Stream>> action, string endpointName, string artifactName, IEndpointInfo source, string contentType)
        {
            _egress = (service, token) => service.EgressAsync(endpointName, action, artifactName, contentType, source, token);
        }

        public EgressStreamResult(Func<Stream, CancellationToken, Task> action, string endpointName, string artifactName, IEndpointInfo source, string contentType)
        {
            _egress = (service, token) => service.EgressAsync(endpointName, action, artifactName, contentType, source, token);
        }

        public override Task ExecuteResultAsync(ActionContext context)
        {
            return context.InvokeAsync(async (token) =>
            {
                IEgressService egressService = context.HttpContext.RequestServices
                    .GetRequiredService<IEgressService>();

                EgressResult egressResult = await _egress(egressService, token);

                IDictionary<string, string> data = new Dictionary<string, string>(StringComparer.Ordinal);
                data.Add(egressResult.Name, egressResult.Value);

                ActionResult jsonResult = new JsonResult(data);

                await jsonResult.ExecuteResultAsync(context);
            });
        }
    }
}

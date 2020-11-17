// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.AspNetCore.Mvc;
using Microsoft.Diagnostics.NETCore.Client;
using System;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.RestServer
{
    internal static class ActionContextExtensions
    {
        public static Task ProblemAsync(this ActionContext context, Exception ex)
        {
            ActionResult result = new BadRequestObjectResult(ex.ToProblemDetails((int)HttpStatusCode.BadRequest));

            return result.ExecuteResultAsync(context);
        }

        public static async Task InvokeAsync(this ActionContext context, Func<CancellationToken, Task> action)
        {
            try
            {
                await action(context.HttpContext.RequestAborted);
            }
            catch (ArgumentException ex)
            {
                await context.ProblemAsync(ex);
            }
            catch (DiagnosticsClientException ex)
            {
                await context.ProblemAsync(ex);
            }
            catch (InvalidOperationException ex)
            {
                await context.ProblemAsync(ex);
            }
            catch (OperationCanceledException ex)
            {
                await context.ProblemAsync(ex);
            }
            catch (MonitoringException ex)
            {
                await context.ProblemAsync(ex);
            }
            catch (ValidationException ex)
            {
                await context.ProblemAsync(ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                await context.ProblemAsync(ex);
            }
        }
    }
}

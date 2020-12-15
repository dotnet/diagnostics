// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.RestServer
{
    internal static class ActionContextExtensions
    {
        private const string ExceptionLogMessage = "Request failed.";

        public static Task ProblemAsync(this ActionContext context, Exception ex)
        {
            if (context.HttpContext.Features.Get<IHttpResponseFeature>().HasStarted)
            {
                // If already started writing response, do not rewrite
                // as this will throw an InvalidOperationException.
                return Task.CompletedTask;
            }
            else
            {
                ActionResult result = new BadRequestObjectResult(ex.ToProblemDetails((int)HttpStatusCode.BadRequest));

                return result.ExecuteResultAsync(context);
            }
        }

        public static async Task InvokeAsync(this ActionContext context, Func<CancellationToken, Task> action, ILogger logger)
        {
            CancellationToken token = context.HttpContext.RequestAborted;
            // Exceptions are logged in the "when" clause in order to preview the exception
            // from the point of where it was thrown. This allows capturing of the log scopes
            // that were active when the exception was thrown. Waiting to log during the exception
            // handler will miss any scopes that were added during invocation of action.
            try
            {
                await action(token);
            }
            catch (ArgumentException ex) when (LogError(logger, ex))
            {
                await context.ProblemAsync(ex);
            }
            catch (DiagnosticsClientException ex) when (LogError(logger, ex))
            {
                await context.ProblemAsync(ex);
            }
            catch (InvalidOperationException ex) when (LogError(logger, ex))
            {
                await context.ProblemAsync(ex);
            }
            catch (OperationCanceledException ex) when (token.IsCancellationRequested && LogInformation(logger, ex))
            {
                await context.ProblemAsync(ex);
            }
            catch (OperationCanceledException ex) when (LogError(logger, ex))
            {
                await context.ProblemAsync(ex);
            }
            catch (MonitoringException ex) when (LogError(logger, ex))
            {
                await context.ProblemAsync(ex);
            }
            catch (ValidationException ex) when (LogError(logger, ex))
            {
                await context.ProblemAsync(ex);
            }
            catch (UnauthorizedAccessException ex) when (LogError(logger, ex))
            {
                await context.ProblemAsync(ex);
            }
        }

        private static bool LogError(ILogger logger, Exception ex)
        {
            logger.LogError(ex, ExceptionLogMessage);
            return true;
        }

        private static bool LogInformation(ILogger logger, Exception ex)
        {
            logger.LogInformation(ex.Message);
            return true;
        }
    }
}

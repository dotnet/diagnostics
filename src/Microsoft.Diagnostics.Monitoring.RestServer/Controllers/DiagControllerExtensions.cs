// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.AspNetCore.Mvc;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

// For more information on enabling MVC for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Microsoft.Diagnostics.Monitoring.RestServer.Controllers
{
    internal static class DiagControllerExtensions
    {
        private const string ExceptionLogMessage = "Request failed.";

        public static ActionResult NotAcceptable(this ControllerBase controller)
        {
            return new StatusCodeResult((int)HttpStatusCode.NotAcceptable);
        }

        public static ActionResult InvokeService(this ControllerBase controller, Func<ActionResult> serviceCall, ILogger logger)
        {
            //We can convert ActionResult to ActionResult<T>
            //and then safely convert back.
            return controller.InvokeService<object>(() => serviceCall(), logger).Result;
        }

        public static ActionResult<T> InvokeService<T>(this ControllerBase controller, Func<ActionResult<T>> serviceCall, ILogger logger)
        {
            //Convert from ActionResult<T> to Task<ActionResult<T>>
            //and safely convert back.
            return controller.InvokeService(() => Task.FromResult(serviceCall()), logger).Result;
        }

        public static async Task<ActionResult> InvokeService(this ControllerBase controller, Func<Task<ActionResult>> serviceCall, ILogger logger)
        {
            //Task<ActionResult> -> Task<ActionResult<T>>
            //Then unwrap the result back to ActionResult
            ActionResult<object> result = await controller.InvokeService<object>(async () => await serviceCall(), logger);
            return result.Result;
        }

        public static async Task<ActionResult<T>> InvokeService<T>(this ControllerBase controller, Func<Task<ActionResult<T>>> serviceCall, ILogger logger)
        {
            CancellationToken token = controller.HttpContext.RequestAborted;
            // Exceptions are logged in the "when" clause in order to preview the exception
            // from the point of where it was thrown. This allows capturing of the log scopes
            // that were active when the exception was thrown. Waiting to log during the exception
            // handler will miss any scopes that were added during invocation of serviceCall.
            try
            {
                return await serviceCall();
            }
            catch (ArgumentException e) when (LogError(logger, e))
            {
                return controller.Problem(e);
            }
            catch (DiagnosticsClientException e) when (LogError(logger, e))
            {
                return controller.Problem(e);
            }
            catch (InvalidOperationException e) when (LogError(logger, e))
            {
                return controller.Problem(e);
            }
            catch (OperationCanceledException e) when (token.IsCancellationRequested && LogInformation(logger, e))
            {
                return controller.Problem(e);
            }
            catch (OperationCanceledException e) when (LogError(logger, e))
            {
                return controller.Problem(e);
            }
            catch (MonitoringException e) when (LogError(logger, e))
            {
                return controller.Problem(e);
            }
            catch (ValidationException e) when (LogError(logger, e))
            {
                return controller.Problem(e);
            }
        }

        public static ObjectResult Problem(this ControllerBase controller, Exception ex)
        {
            return controller.BadRequest(ex.ToProblemDetails((int)HttpStatusCode.BadRequest));
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

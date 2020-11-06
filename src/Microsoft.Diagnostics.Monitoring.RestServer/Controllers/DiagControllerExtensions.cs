// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.AspNetCore.Mvc;
using Microsoft.Diagnostics.NETCore.Client;
using System;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Threading.Tasks;

// For more information on enabling MVC for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Microsoft.Diagnostics.Monitoring.RestServer.Controllers
{
    internal static class DiagControllerExtensions
    {
        public static ActionResult NotAcceptable(this ControllerBase controller)
        {
            return new StatusCodeResult((int)HttpStatusCode.NotAcceptable);
        }

        public static ActionResult InvokeService(this ControllerBase controller, Func<ActionResult> serviceCall)
        {
            //We can convert ActionResult to ActionResult<T>
            //and then safely convert back.
            return controller.InvokeService<object>(() => serviceCall()).Result;
        }

        public static ActionResult<T> InvokeService<T>(this ControllerBase controller, Func<ActionResult<T>> serviceCall)
        {
            //Convert from ActionResult<T> to Task<ActionResult<T>>
            //and safely convert back.
            return controller.InvokeService(() => Task.FromResult(serviceCall())).Result;
        }

        public static async Task<ActionResult> InvokeService(this ControllerBase controller, Func<Task<ActionResult>> serviceCall)
        {
            //Task<ActionResult> -> Task<ActionResult<T>>
            //Then unwrap the result back to ActionResult
            ActionResult<object> result = await controller.InvokeService<object>(async () => await serviceCall());
            return result.Result;
        }

        public static async Task<ActionResult<T>> InvokeService<T>(this ControllerBase controller, Func<Task<ActionResult<T>>> serviceCall)
        {
            try
            {
                return await serviceCall();
            }
            catch (ArgumentException e)
            {
                return controller.Problem(e);
            }
            catch (DiagnosticsClientException e)
            {
                return controller.Problem(e);
            }
            catch (InvalidOperationException e)
            {
                return controller.Problem(e);
            }
            catch (OperationCanceledException e)
            {
                return controller.Problem(e);
            }
            catch (MonitoringException e)
            {
                return controller.Problem(e);
            }
            catch (ValidationException e)
            {
                return controller.Problem(e);
            }
        }

        public static ObjectResult Problem(this ControllerBase controller, Exception ex)
        {
            return controller.BadRequest(ex.ToProblemDetails((int)HttpStatusCode.BadRequest));
        }
    }
}

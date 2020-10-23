// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Diagnostics.NETCore.Client;

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
                return controller.BadRequest(FromException(e));
            }
            catch (DiagnosticsClientException e)
            {
                return controller.BadRequest(FromException(e));
            }
            catch (InvalidOperationException e)
            {
                return controller.BadRequest(FromException(e));
            }
            catch (PipelineException e)
            {
                return controller.BadRequest(FromException(e));
            }
        }

        private static ProblemDetails FromException(Exception e)
        {
            return new ProblemDetails
            {
                Detail = e.Message,
                Status = (int)HttpStatusCode.BadRequest
            };
        }
    }
}

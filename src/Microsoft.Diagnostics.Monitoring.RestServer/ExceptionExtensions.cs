// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.AspNetCore.Mvc;
using System;

namespace Microsoft.Diagnostics.Monitoring.RestServer
{
    internal static class ExceptionExtensions
    {
        public static ProblemDetails ToProblemDetails(this Exception ex, int statusCode)
        {
            return new ProblemDetails
            {
                Detail = ex.Message,
                Status = statusCode
            };
        }
    }
}

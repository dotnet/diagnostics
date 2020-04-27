// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.AspNetCore.Http;
using Microsoft.Diagnostics.Monitoring.Contracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring
{
    /// <summary>
    /// Provides direct access to the Response stream for underlying services.
    /// </summary>
    internal sealed class StreamAccessorService : IStreamAccessor
    {
        private readonly IHttpContextAccessor _contextAccessor;
        public StreamAccessorService(IHttpContextAccessor contextAccessor)
        {
            _contextAccessor = contextAccessor;
        }

        public Stream OutputStream => _contextAccessor.HttpContext?.Response.Body;
    }
}

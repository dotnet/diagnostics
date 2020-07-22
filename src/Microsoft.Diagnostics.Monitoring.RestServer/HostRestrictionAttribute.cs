// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.RestServer
{
    /// <summary>
    /// We want to restrict the Prometheus scraping endpoint to only the /metrics call.
    /// To do this, we determine what port the request is on, and disallow other actions on the prometheus port.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    internal sealed class HostRestrictionAttribute : Attribute, IActionConstraintFactory
    {
        private sealed class HostConstraint : IActionConstraint
        {
            private readonly int?[] _restrictedPorts;

            public HostConstraint(int?[] restrictedPorts)
            {
                _restrictedPorts = restrictedPorts;
            }

            public int Order => 0;

            public bool Accept(ActionConstraintContext context)
            {
                return !_restrictedPorts.Any(port => context.RouteContext.HttpContext.Request.Host.Port == port);
            }
        }

        public bool IsReusable => true;

        public IActionConstraint CreateInstance(IServiceProvider services)
        {
            var metricOptions = services.GetRequiredService<IOptions<PrometheusConfiguration>>();
            return new HostConstraint(metricOptions.Value.Enabled ? metricOptions.Value.Ports : Array.Empty<int?>());
        }
    }
}

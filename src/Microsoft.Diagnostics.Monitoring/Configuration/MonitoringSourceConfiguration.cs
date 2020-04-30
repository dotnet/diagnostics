// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.NETCore.Client;
using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring
{
    public abstract class MonitoringSourceConfiguration
    {
        public const string MicrosoftExtensionsLoggingProviderName = "Microsoft-Extensions-Logging";
        public const string SystemRuntimeEventSourceName = "System.Runtime";
        public const string MicrosoftAspNetCoreHostingEventSourceName = "Microsoft.AspNetCore.Hosting";
        public const string GrpcAspNetCoreServer = "Grpc.AspNetCore.Server";
        public const string DiagnosticSourceEventSource = "Microsoft-Diagnostics-DiagnosticSource";
        public const string TplEventSource = "System.Threading.Tasks.TplEventSource";

        public abstract IList<EventPipeProvider> GetProviders();
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Monitoring.RestServer
{
    /// <summary>
    /// Configuration for prometheus metric collection and retrieval.
    /// TODO We may want to expose https endpoints here as well, and make port changes
    /// TODO How do we determine which process to scrape in multi-proc situations? How do we configure this
    /// for situations where the pid is not known or ambiguous?
    /// </summary>
    public class PrometheusConfiguration
    {
        private readonly Lazy<Uri> _endpoint;
        
        public PrometheusConfiguration()
        {
            _endpoint = new Lazy<Uri>(() => new Uri(Endpoint));
        }

        public bool Enabled { get; set; }
        
        public string Endpoint { get; set; }

        public Uri EndpointAddress => _endpoint.Value;

        public int UpdateIntervalSeconds { get; set; }
    }
}

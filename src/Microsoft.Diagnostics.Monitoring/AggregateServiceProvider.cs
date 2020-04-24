// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.Monitoring
{
    /// <summary>
    /// Allows chaining of service providers.
    /// </summary>
    public class AggregateServiceProvider : IServiceProvider
    {
        private readonly IServiceProvider _parent;
        private readonly IServiceProvider _current;

        public AggregateServiceProvider(IServiceProvider parent, IServiceProvider current)
        {
            _parent = parent;
            _current = current;
        }

        public object GetService(Type serviceType)
        {
            object service = _current.GetService(serviceType);
            if (service == null)
            {
                service = _parent.GetService(serviceType);
            }
            return service;
        }
    }
}

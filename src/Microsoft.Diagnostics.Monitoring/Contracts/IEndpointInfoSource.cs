// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Diagnostics.Monitoring
{
    internal interface IEndpointInfo
    {
        IpcEndpoint Endpoint { get; }

        int ProcessId { get; }

        Guid RuntimeInstanceCookie { get; }

        string CommandLine { get; }

        string OperatingSystem { get; }

        string ProcessArchitecture { get; }
    }

    public interface IEndpointInfoSource
    {
    }

    internal interface IEndpointInfoSourceInternal : IEndpointInfoSource
    {
        Task<IEnumerable<IEndpointInfo>> GetEndpointInfoAsync(CancellationToken token);
    }
}

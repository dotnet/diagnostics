// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using Microsoft.Diagnostics.NETCore.Client;

namespace Microsoft.Diagnostics.Monitoring
{
    [DebuggerDisplay("{DebuggerDisplay,nq}")]
    internal class EndpointInfo : IEndpointInfo
    {
        public static EndpointInfo FromProcessId(int processId)
        {
            var client = new DiagnosticsClient(processId);

            ProcessInfo processInfo = null;
            try
            {
                // Primary motivation is to get the runtime instance cookie in order to
                // keep parity with the FromIpcEndpointInfo implementation; store the
                // remainder of the information since it already has access to it.
                processInfo = client.GetProcessInfo();

                Debug.Assert(processId == unchecked((int)processInfo.ProcessId));
            }
            catch (ServerErrorException)
            {
                // The runtime likely doesn't understand the GetProcessInfo command.
            }
            catch (TimeoutException)
            {
                // Runtime didn't respond within client timeout.
            }

            // CONSIDER: Generate a runtime instance identifier based on the pipe name
            // for .NET Core 3.1 e.g. pid + disambiguator in GUID form.
            return new EndpointInfo()
            {
                Endpoint = new PidIpcEndpoint(processId),
                ProcessId = processId,
                RuntimeInstanceCookie = processInfo?.RuntimeInstanceCookie ?? Guid.Empty,
                CommandLine = processInfo?.CommandLine,
                OperatingSystem = processInfo?.OperatingSystem,
                ProcessArchitecture = processInfo?.ProcessArchitecture
            };
        }

        public static EndpointInfo FromIpcEndpointInfo(IpcEndpointInfo info)
        {
            var client = new DiagnosticsClient(info.Endpoint);

            ProcessInfo processInfo = null;
            try
            {
                // Primary motivation is to keep parity with the FromProcessId implementation,
                // which provides the additional process information because it already has
                // access to it.
                processInfo = client.GetProcessInfo();

                Debug.Assert(info.ProcessId == unchecked((int)processInfo.ProcessId));
                Debug.Assert(info.RuntimeInstanceCookie == processInfo.RuntimeInstanceCookie);
            }
            catch (ServerErrorException)
            {
                // The runtime likely doesn't understand the GetProcessInfo command.
            }
            catch (TimeoutException)
            {
                // Runtime didn't respond within client timeout.
            }

            return new EndpointInfo()
            {
                Endpoint = info.Endpoint,
                ProcessId = info.ProcessId,
                RuntimeInstanceCookie = info.RuntimeInstanceCookie,
                CommandLine = processInfo?.CommandLine,
                OperatingSystem = processInfo?.OperatingSystem,
                ProcessArchitecture = processInfo?.ProcessArchitecture
            };
        }

        public IpcEndpoint Endpoint { get; private set; }

        public int ProcessId { get; private set; }

        public Guid RuntimeInstanceCookie { get; private set; }

        public string CommandLine { get; private set; }

        public string OperatingSystem { get; private set; }

        public string ProcessArchitecture { get; private set; }

        internal string DebuggerDisplay => FormattableString.Invariant($"PID={ProcessId}, Cookie={RuntimeInstanceCookie}");
    }
}

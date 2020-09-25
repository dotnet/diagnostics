// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Runtime.InteropServices;
using Xunit.Abstractions;

namespace Microsoft.Diagnostics.NETCore.Client
{
    internal static class ReversedServerHelper
    {
        /// <summary>
        /// Creates a unique server name to avoid collisions from simultaneous running tests
        /// or potentially abandoned socket files.
        /// </summary>
        public static string CreateServerTransportName()
        {
            string transportName = "DOTNET_DIAGSERVER_TESTS_" + Path.GetRandomFileName();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return transportName;
            }
            else
            {
                return Path.Combine(Path.GetTempPath(), transportName);
            }
        }

        /// <summary>
        /// Starts the Tracee executable while enabling connection to reverse diagnostics server.
        /// </summary>
        public static TestRunner StartTracee(ITestOutputHelper _outputHelper, string transportName)
        {
            var runner = new TestRunner(CommonHelper.GetTraceePathWithArgs(targetFramework: "net5.0"), _outputHelper);
            runner.AddReversedServer(transportName);
            runner.Start();
            return runner;
        }

        public static void AddReversedServer(this TestRunner runner, string transportName)
        {
            runner.AddEnvVar("DOTNET_DiagnosticsMonitorAddress", transportName);
            runner.AddEnvVar("DOTNET_DiagnosticPorts", $"{transportName},nosuspend;");
        }
    }
}

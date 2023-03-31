// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.CommonTestRunner
{
    public static class DiagnosticPortsHelper
    {
        private const string DiagnosticPortsEnvName = "DOTNET_DiagnosticPorts";
        private const string DefaultDiagnosticPortSuspendEnvName = "DOTNET_DefaultDiagnosticPortSuspend";

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

        public static void SetDiagnosticPort(this TestRunner runner, string transportName, bool suspend)
        {
            string suspendArgument = suspend ? "suspend" : "nosuspend";
            runner.AddEnvVar(DiagnosticPortsEnvName, $"{transportName},connect,{suspendArgument};");
        }

        public static void SuspendDefaultDiagnosticPort(this TestRunner runner)
        {
            runner.AddEnvVar(DefaultDiagnosticPortSuspendEnvName, "1");
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.NETCore.Client
{
    public partial class CommonHelper
    {
        public static string HostExe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? 
            (RuntimeInformation.ProcessArchitecture == Architecture.X86 ? 
                "..\\..\\..\\..\\..\\.dotnet\\x86\\dotnet.exe" : 
                "..\\..\\..\\..\\..\\.dotnet\\dotnet.exe") : 
            "../../../../../.dotnet/dotnet";
        
        public static string GetTraceePath(string traceeName = "Tracee", string targetFramework = "netcoreapp3.1")
        {
            var curPath = Directory.GetCurrentDirectory();

            var traceePath = curPath
                .Replace(System.Reflection.Assembly.GetCallingAssembly().GetName().Name, traceeName)
                .Replace("netcoreapp3.1", targetFramework);

            return Path.Combine(traceePath, Path.ChangeExtension(traceeName, ".dll"));
        }

        /// <summary>
        /// gets the tracee path, with args for the dotnet host for finding the correct version of the runtime.!--
        /// example: "--fx-version 5.0.0-rc.1.12345.12 /path/to/tracee"
        /// </summary>
        public static string GetTraceePathWithArgs(string traceeName = "Tracee", string targetFramework = "netcoreapp3.1")
        {
            string traceePath = GetTraceePath(traceeName, targetFramework);

            // CurrentDARCVersion is generated at build time by Microsoft.Diagnostics.NETCore.Client.UnitTests.csproj
            // This value will be set to whatever the value for the newest runtime in eng/Versions.Props is
            if (targetFramework.Equals("net5.0", StringComparison.InvariantCultureIgnoreCase))
                traceePath = $"--fx-version {CurrentDARCVersion} {traceePath}";

            return traceePath;
        }
    }
}
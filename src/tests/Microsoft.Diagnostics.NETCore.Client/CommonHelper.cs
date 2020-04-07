// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

using Microsoft.Diagnostics.TestHelpers;

namespace Microsoft.Diagnostics.NETCore.Client
{
    public class CommonHelper
    {
        public static string HostExe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? 
            "..\\..\\..\\..\\..\\.dotnet\\dotnet.exe" : "../../../../../.dotnet/dotnet";
        
        public static string GetTraceePath()
        {
            var curPath = Directory.GetCurrentDirectory();
;
            var traceePath = curPath.Replace("Microsoft.Diagnostics.NETCore.Client.UnitTests", "Tracee");

            return Path.Combine(traceePath, "Tracee.dll");
        }
    }
}
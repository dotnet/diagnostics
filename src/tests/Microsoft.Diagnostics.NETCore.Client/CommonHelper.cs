// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.NETCore.Client
{
    public class CommonHelper
    {
        public static string HostExe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? 
            (RuntimeInformation.ProcessArchitecture == Architecture.X86 ? 
                "..\\..\\..\\..\\..\\.dotnet\\x86\\dotnet.exe" : 
                "..\\..\\..\\..\\..\\.dotnet\\dotnet.exe") : 
            "../../../../../.dotnet/dotnet";
        
        public static string GetTraceePath(string traceeName = "Tracee")
        {
            var curPath = Directory.GetCurrentDirectory();
;
            var traceePath = curPath.Replace(System.Reflection.Assembly.GetCallingAssembly().GetName().Name, traceeName);

            return Path.Combine(traceePath, Path.ChangeExtension(traceeName, ".dll"));
        }
    }
}
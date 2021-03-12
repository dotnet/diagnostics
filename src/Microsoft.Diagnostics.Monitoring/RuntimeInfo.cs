﻿using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Monitoring
{
    public static class RuntimeInfo
    {
        public static bool IsDiagnosticsEnabled
        {
            get
            {
                string enableDiagnostics = Environment.GetEnvironmentVariable("COMPlus_EnableDiagnostics");
                return string.IsNullOrEmpty(enableDiagnostics) || !"0".Equals(enableDiagnostics, StringComparison.Ordinal);
            }
        }

        public static bool IsInDockerContainer
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    // Check if one of the control groups of this process is owned by docker
                    if (File.ReadAllText("/proc/self/cgroup").Contains("/docker/"))
                    {
                        return true;
                    }

                    // Most of the control groups are owned by "kubepods" when running in kubernetes;
                    // Check for docker environment file
                    return File.Exists("/.dockerenv");
                }

                // TODO: Add detection for other platforms
                return false;
            }
        }

        public static bool IsInKubernetes => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST"));
    }
}
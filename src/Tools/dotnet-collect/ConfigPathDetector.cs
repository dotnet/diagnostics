using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Tools.Collect
{
    internal static class ConfigPathDetector
    {
        private static readonly HashSet<string> _managedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".exe", ".dll" };

        // Known .NET Platform Assemblies
        private static readonly HashSet<string> _platformAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System.Private.CoreLib.dll",
            "clrjit.dll",
        };

        internal static string TryDetectConfigPath(int processId)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Windows.TryDetectConfigPath(processId);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return Linux.TryDetectConfigPath(processId);
            }
            return null;
        }

        private static class Linux
        {
            public static string TryDetectConfigPath(int processId)
            {
                // Read procfs maps list
                var lines = File.ReadAllLines($"/proc/{processId}/maps");

                foreach (var line in lines)
                {
                    try
                    {
                        var parser = new StringParser(line, separator: ' ', skipEmpty: true);

                        // Skip the address range
                        parser.MoveNext();

                        var permissions = parser.MoveAndExtractNext();

                        // The managed entry point is Read-Only, Non-Execute and Shared.
                        if (!string.Equals(permissions, "r--s", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        // Skip offset, dev, and inode
                        parser.MoveNext();
                        parser.MoveNext();
                        parser.MoveNext();

                        // Parse the path
                        if (!parser.MoveNext())
                        {
                            continue;
                        }

                        var path = parser.ExtractCurrentToEnd();
                        var candidateDir = Path.GetDirectoryName(path);
                        var candidateName = Path.GetFileNameWithoutExtension(path);
                        if (File.Exists(Path.Combine(candidateDir, $"{candidateName}.deps.json")))
                        {
                            return Path.Combine(candidateDir, $"{candidateName}.eventpipeconfig");
                        }
                    }
                    catch (Exception)
                    {
                        // Suppress exception and just try the next entry.
                    }
                }
                return null;
            }
        }

        private static class Windows
        {
            private static readonly HashSet<string> _knownNativeLibraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // .NET Core Host
                "dotnet.exe",
                "hostfxr.dll",
                "hostpolicy.dll",
                "coreclr.dll",

                // Windows Native Libraries
                "ntdll.dll",
                "kernel32.dll",
                "kernelbase.dll",
                "apphelp.dll",
                "ucrtbase.dll",
                "advapi32.dll",
                "msvcrt.dll",
                "sechost.dll",
                "rpcrt4.dll",
                "ole32.dll",
                "combase.dll",
                "bcryptPrimitives.dll",
                "gdi32.dll",
                "gdi32full.dll",
                "msvcp_win.dll",
                "user32.dll",
                "win32u.dll",
                "oleaut32.dll",
                "shlwapi.dll",
                "version.dll",
                "bcrypt.dll",
                "imm32.dll",
                "kernel.appcore.dll",
            };

            public static string TryDetectConfigPath(int processId)
            {
                var process = Process.GetProcessById(processId);

                // Iterate over modules
                foreach (var module in process.Modules.Cast<ProcessModule>())
                {
                    // Filter out things that aren't exes and dlls (useful on Unix/macOS to skip native libraries)
                    var extension = Path.GetExtension(module.FileName);
                    var name = Path.GetFileName(module.FileName);
                    if (_managedExtensions.Contains(extension) && !_knownNativeLibraries.Contains(name) && !_platformAssemblies.Contains(name))
                    {
                        var candidateDir = Path.GetDirectoryName(module.FileName);
                        var appName = Path.GetFileNameWithoutExtension(module.FileName);

                        // Check for the deps.json file
                        // TODO: Self-contained apps?
                        if (File.Exists(Path.Combine(candidateDir, $"{appName}.deps.json")))
                        {
                            // This is an app!
                            return Path.Combine(candidateDir, $"{appName}.eventpipeconfig");
                        }
                    }
                }

                return null;
            }
        }
    }
}

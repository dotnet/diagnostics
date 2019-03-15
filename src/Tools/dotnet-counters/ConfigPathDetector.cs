// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Tools.Counters
{
    // NOTE: This is copied over from dotnet-collect.
    // This will eventually go away once we get the runtime ready to stream events via IPC.
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
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return OSX.TryDetectConfigPath(processId);
            }
            return null;
        }

        private static class OSX
        {
            // This is defined in proc_info.h (https://opensource.apple.com/source/xnu/xnu-1228/bsd/sys/proc_info.h)
            private const int PROC_PIDPATHINFO_MAXSIZE = 1024 * 4;

            /// <summary>
            /// Gets the full path to the executable file identified by the specified PID
            /// </summary>
            /// <param name="pid">The PID of the running process</param>
            /// <param name="buffer">A pointer to an allocated block of memory that will be filled with the process path</param>
            /// <param name="bufferSize">The size of the buffer, should be PROC_PIDPATHINFO_MAXSIZE</param>
            /// <returns>Returns the length of the path returned on success</returns>
            [DllImport("libproc.dylib", SetLastError = true)]
            private static extern unsafe int proc_pidpath(
                int pid, 
                byte* buffer, 
                uint bufferSize);

            /// <summary>
            /// Gets the full path to the executable file identified by the specified PID
            /// </summary>
            /// <param name="pid">The PID of the running process</param>
            /// <returns>Returns the full path to the process executable</returns>
            internal static unsafe string proc_pidpath(int pid)
            {
                // The path is a fixed buffer size, so use that and trim it after
                int result = 0;
                byte* pBuffer = stackalloc byte[PROC_PIDPATHINFO_MAXSIZE];

                // WARNING - Despite its name, don't try to pass in a smaller size than specified by PROC_PIDPATHINFO_MAXSIZE.
                // For some reason libproc returns -1 if you specify something that's NOT EQUAL to PROC_PIDPATHINFO_MAXSIZE
                // even if you declare your buffer to be smaller/larger than this size. 
                result = proc_pidpath(pid, pBuffer, (uint)(PROC_PIDPATHINFO_MAXSIZE * sizeof(byte)));
                if (result <= 0)
                {
                    throw new InvalidOperationException("Could not find procpath using libproc.");
                }

                // OS X uses UTF-8. The conversion may not strip off all trailing \0s so remove them here
                return System.Text.Encoding.UTF8.GetString(pBuffer, result);
            }

            public static string TryDetectConfigPath(int processId)
            {
                try
                {
                    var path = proc_pidpath(processId);
                    var candidateDir = Path.GetDirectoryName(path);
                    var candidateName = Path.GetFileNameWithoutExtension(path);
                    return Path.Combine(candidateDir, $"{candidateName}.eventpipeconfig");
                }
                catch (InvalidOperationException)
                {
                    return null;  // The pinvoke above may fail - return null in that case to handle error gracefully.
                }
            }
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
                    catch (ArgumentNullException) { return null; }
                    catch (InvalidDataException) { return null; }
                    catch (InvalidOperationException) { return null; }
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
                try
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
                }
                catch (ArgumentException)
                {
                    return null;
                }

                return null;
            }
        }
    }
}

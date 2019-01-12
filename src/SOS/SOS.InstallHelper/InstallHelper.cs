// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SOS.InstallHelper
{
    /// <summary>
    /// Functions to install and configure SOS from the package containing this code.
    /// </summary>
    public sealed class InstallHelper
    {
        /// <summary>
        /// Well known location to install SOS. Defaults to $HOME/.dotnet/sos on xplat and %USERPROFILE%/.dotnet/sos on Windows.
        /// </summary>
        public string InstallLocation { get; set; }

        /// <summary>
        /// On Linux/MacOS, the location of the lldb ".lldbinit" file. Defaults to $HOME/.lldbinit.
        /// </summary>
        public string LLDBInitFile { get; set; }

        /// <summary>
        /// If true, enable the symbol server support when configuring lldb.
        /// </summary>
        public bool EnableSymbolServer { get; set; } = true;

        /// <summary>
        /// The source path from which SOS is installed. Default is OS/architecture (RID) named directory in the same directory as this assembly.
        /// </summary>
        public string SOSSourcePath { get; set; }

        /// <summary>
        /// Create an instance of the installer.
        /// </summary>
        /// <exception cref="PlatformNotSupportedException">unknown operating system</exception>
        public InstallHelper()
        {
            string home = null;
            string os = null;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) 
            {
                home = Environment.GetEnvironmentVariable("USERPROFILE");
                os = "win";
            }
            else
            {
                home = Environment.GetEnvironmentVariable("HOME");
                LLDBInitFile = Path.Combine(home, ".lldbinit");

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                    os = "osx";
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                    os = "linux";
                }
            }
            if (os == null) {
                throw new PlatformNotSupportedException($"Unsupported operating system {RuntimeInformation.OSDescription}");
            }
            Debug.Assert(!string.IsNullOrEmpty(home));
            InstallLocation = Path.GetFullPath(Path.Combine(home, ".dotnet", "sos"));

            string architecture = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
            string rid = os + "-" + architecture;
            SOSSourcePath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), rid);
        }

        /// <summary>
        /// Install SOS to well known location (InstallLocation).
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="PlatformNotSupportedException">SOS not found for OS/architecture</exception>
        public void Install()
        {
            Debug.Assert(!string.IsNullOrEmpty(InstallLocation));
            Debug.Assert(!string.IsNullOrEmpty(SOSSourcePath));
            if (!Directory.Exists(SOSSourcePath)) {
                throw new PlatformNotSupportedException($"Operating system or architecture not supported: installing from {SOSSourcePath}");
            }
            Directory.CreateDirectory(InstallLocation);
            foreach (string file in Directory.EnumerateFiles(SOSSourcePath))
            {
                string destinationFile = Path.Combine(InstallLocation, Path.GetFileName(file));
                File.Copy(file, destinationFile, overwrite: true);
            }
        }

        /// <summary>
        /// Uninstalls and removes the SOS configuration.
        /// </summary>
        public void Uninstall()
        {
            if (!string.IsNullOrEmpty(LLDBInitFile)) {
                Configure(remove: true);
            }
            if (Directory.Exists(InstallLocation))
            {
                foreach (string file in Directory.EnumerateFiles(InstallLocation))
                {
                    File.Delete(file);
                }
                Directory.Delete(InstallLocation);
            }
        }

        const string InitFileStart = "#START - ADDED BY SOS INSTALLER";
        const string InitFileEnd = "#END - ADDED BY SOS INSTALLER";

        /// <summary>
        /// Configure lldb to load SOS.
        /// </summary>
        /// <param name="remove">if true, remove the configuration from the init file</param>
        /// <exception cref="ArgumentException"></exception>
        public void Configure(bool remove = false)
        {
            if (string.IsNullOrEmpty(LLDBInitFile)) {
                throw new ArgumentException("No lldb configuration file");
            }

            // Remove the start/end marker from an existing .lldbinit file
            var lines = new List<string>();
            if (File.Exists(LLDBInitFile))
            {
                bool markerFound = false;
                foreach (string line in File.ReadAllLines(LLDBInitFile))
                {
                    if (line.Contains(InitFileEnd)) {
                        markerFound = false;
                        continue;
                    }
                    if (!markerFound) {
                        if (line.Contains(InitFileStart)) {
                            markerFound = true;
                            continue;
                        }
                        lines.Add(line);
                    }
                }
                if (markerFound) {
                    throw new ArgumentException(".lldbinit file end marker not found");
                }
            }

            // If configure (not remove), add the plugin load, etc. configuration between the start/end markers.
            if (!remove)
            {
                lines.Add(InitFileStart);
                string plugin = Path.Combine(InstallLocation, "libsosplugin");
                string extension = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ".dylib" : ".so";
                lines.Add($"plugin load {plugin}{extension}");

                if (EnableSymbolServer) {
                    lines.Add(string.Format("setsymbolserver -ms"));
                }
                lines.Add(InitFileEnd);
            }

            // If there is anything to write, write the lldb init file
            if (lines.Count > 0) {
                File.WriteAllLines(LLDBInitFile, lines.ToArray());
            }
        }
    }
}
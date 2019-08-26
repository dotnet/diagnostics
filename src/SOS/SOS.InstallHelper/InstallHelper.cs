﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;

namespace SOS
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
        /// Console output delegate
        /// </summary>
        private readonly Action<string> m_writeLine;

        /// <summary>
        /// Create an instance of the installer.
        /// </summary>
        /// <param name="writeLine">console output delegate</param>
        /// <param name="architecture">architecture to install or if null using the current process architecture</param>
        /// <exception cref="SOSInstallerException">environment variable not found</exception>
        public InstallHelper(Action<string> writeLine, Architecture? architecture = null)
        {
            m_writeLine = writeLine;
            string rid = GetRid(architecture);
            string home;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) 
            {
                home = Environment.GetEnvironmentVariable("USERPROFILE");
                if (string.IsNullOrEmpty(home)) {
                    throw new SOSInstallerException("USERPROFILE environment variable not found");
                }
            }
            else
            {
                home = Environment.GetEnvironmentVariable("HOME");
                if (string.IsNullOrEmpty(home)) {
                    throw new SOSInstallerException("HOME environment variable not found");
                }
                LLDBInitFile = Path.Combine(home, ".lldbinit");
            }
            InstallLocation = Path.GetFullPath(Path.Combine(home, ".dotnet", "sos"));
            SOSSourcePath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), rid);
        }

        /// <summary>
        /// Install SOS to well known location (InstallLocation).
        /// </summary>
        /// <exception cref="SOSInstallerException">various</exception>
        public void Install()
        {
            WriteLine("Installing SOS to {0} from {1}", InstallLocation, SOSSourcePath);

            if (string.IsNullOrEmpty(SOSSourcePath)) {
                throw new SOSInstallerException("SOS source path not valid");
            }
            if (!Directory.Exists(SOSSourcePath)) {
                throw new SOSInstallerException($"Operating system or architecture not supported: installing from {SOSSourcePath}");
            }
            if (string.IsNullOrEmpty(InstallLocation)) {
                throw new SOSInstallerException($"Installation path {InstallLocation} not valid");
            }

            // Rename any existing installation
            string previousInstall = null;
            if (Directory.Exists(InstallLocation))
            {
                WriteLine("Installing over existing installation...");
                previousInstall = Path.Combine(Path.GetDirectoryName(InstallLocation), Path.GetRandomFileName());
                RetryOperation($"Installation path '{InstallLocation}' not valid", () => Directory.Move(InstallLocation, previousInstall));
            }

            bool installSuccess = false;
            try
            {
                // Create the installation directory
                WriteLine("Creating installation directory...");
                RetryOperation($"Installation path '{InstallLocation}' not valid", () => Directory.CreateDirectory(InstallLocation));

                // Copy SOS files
                WriteLine("Copying files...");
                RetryOperation("Problem installing SOS", () =>
                {
                    foreach (string file in Directory.EnumerateFiles(SOSSourcePath))
                    {
                        string destinationFile = Path.Combine(InstallLocation, Path.GetFileName(file));
                        File.Copy(file, destinationFile, overwrite: true);
                    }
                });

                // Configure lldb 
                if (LLDBInitFile != null) {
                    Configure();
                }
                else {
                    WriteLine($"Execute '.load {InstallLocation}\\sos.dll' to load SOS in your Windows debugger.");
                }

                // If we get here without an exception, success!
                installSuccess = true;
            }
            finally
            {
                if (previousInstall != null)
                {
                    WriteLine("Cleaning up...");
                    if (installSuccess)
                    {
                        // Delete the previous installation if the install was successful
                        RetryOperation(null, () => Directory.Delete(previousInstall, recursive: true));
                    }
                    else
                    {
                        // Delete partial installation
                        RetryOperation(null, () => Directory.Delete(InstallLocation, recursive: true));

                        // Restore previous install
                        WriteLine("Restoring previous installation...");
                        RetryOperation(null, () => Directory.Move(previousInstall, InstallLocation));
                    }
                }
            }

            Debug.Assert(installSuccess);
            WriteLine("SOS install succeeded");
        }

        /// <summary>
        /// Uninstalls and removes the SOS configuration.
        /// </summary>
        /// <exception cref="SOSInstallerException">various</exception>
        public void Uninstall()
        {
            WriteLine("Uninstalling SOS from {0}", InstallLocation);
            if (!string.IsNullOrEmpty(LLDBInitFile))
            {
                Configure(remove: true);
            }
            if (Directory.Exists(InstallLocation))
            {
                RetryOperation("Problem uninstalling SOS", () => Directory.Delete(InstallLocation, recursive: true));
                WriteLine("SOS uninstall succeeded");
            }
            else
            {
                WriteLine("SOS not installed");
            }
        }

        const string InitFileStart = "#START - ADDED BY SOS INSTALLER";
        const string InitFileEnd = "#END - ADDED BY SOS INSTALLER";

        /// <summary>
        /// Configure lldb to load SOS.
        /// </summary>
        /// <param name="remove">if true, remove the configuration from the init file</param>
        /// <exception cref="SOSInstallerException"></exception>
        public void Configure(bool remove = false)
        {
            if (string.IsNullOrEmpty(LLDBInitFile)) {
                throw new SOSInstallerException("No lldb configuration file path");
            }
            bool changed = false;
            bool existing = false;

            // Remove the start/end marker from an existing .lldbinit file
            var lines = new List<string>();
            if (File.Exists(LLDBInitFile))
            {
                existing = true;

                bool markerFound = false;
                string[] contents = null;
                RetryOperation($"Problem reading lldb init file {LLDBInitFile}", () => contents = File.ReadAllLines(LLDBInitFile));

                foreach (string line in contents)
                {
                    if (line.Contains(InitFileEnd))
                    {
                        markerFound = false;
                        changed = true;
                        continue;
                    }
                    if (!markerFound)
                    {
                        if (line.Contains(InitFileStart))
                        {
                            markerFound = true;
                            changed = true;
                            continue;
                        }
                        lines.Add(line);
                    }
                }

                if (markerFound) {
                    throw new SOSInstallerException(".lldbinit file end marker not found");
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
                changed = true;
            }

            // If there is anything to write, write the lldb init file
            if (changed)
            {
                if (remove) {
                    WriteLine("Reverting {0} file - LLDB will no longer load SOS at startup", LLDBInitFile);
                }
                else {
                    WriteLine("{0} {1} file - LLDB will load SOS automatically at startup", existing ? "Updating existing" : "Creating new", LLDBInitFile);
                }
                RetryOperation($"Problem writing lldb init file {LLDBInitFile}", () => File.WriteAllLines(LLDBInitFile, lines.ToArray()));
            }
        }

        /// <summary>
        /// Retries any IO operation failures.
        /// </summary>
        /// <param name="errorMessage">text message or null (don't throw exception)</param>
        /// <param name="operation">callback</param>
        /// <exception cref="SOSInstallerException">errorMessage</exception>
        private void RetryOperation(string errorMessage, Action operation)
        {
            Exception lastfailure = null;

            for (int retry = 0; retry < 5; retry++)
            {
                try
                {
                    operation();
                    return;
                }
                catch (Exception ex) when (ex is IOException)
                {
                    // Retry possible recoverable exception
                    lastfailure = ex;

                    // Sleep to allow any temporary error condition to clear up
                    System.Threading.Thread.Sleep(1000);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is UnauthorizedAccessException || ex is SecurityException)
                {
                    if (errorMessage == null) {
                        return;
                    }
                    throw new SOSInstallerException($"{errorMessage}: {ex.Message}", ex);
                }
            }

            if (lastfailure != null)
            {
                if (errorMessage == null) {
                    return;
                }
                throw new SOSInstallerException($"{errorMessage}: {lastfailure.Message}", lastfailure);
            }
        }

        /// <summary>
        /// Returns the RID
        /// </summary>
        /// <param name="architecture">architecture to install or if null using the current process architecture</param>
        public static string GetRid(Architecture? architecture = null)
        {
            string os = null;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                os = "win";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                os = "osx";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                os = "linux";
                try
                {
                    string ostype = File.ReadAllText("/etc/os-release");
                    if (ostype.Contains("ID=alpine"))
                    {
                        os = "linux-musl";
                    }
                }
                catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException || ex is IOException)
                {
                }
            }
            if (os == null)
            {
                throw new SOSInstallerException($"Unsupported operating system {RuntimeInformation.OSDescription}");
            }
            string architectureString = (architecture.HasValue ? architecture : RuntimeInformation.ProcessArchitecture).ToString().ToLowerInvariant();
            return $"{os}-{architectureString}";
        }

        private void WriteLine(string format, params object[] args)
        {
            m_writeLine?.Invoke(string.Format(format, args));
        }
    }

    /// <summary>
    /// SOS installer error
    /// </summary>
    public class SOSInstallerException : Exception
    {
        public SOSInstallerException(string message)
            : base(message)
        {
        }

        public SOSInstallerException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
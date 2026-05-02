// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.TestHelpers;
using Microsoft.Win32;

/// <summary>
/// Registers Windows dump generation registry keys once per test assembly and
/// cleans them up at process exit. Uses [ModuleInitializer] so that setup runs
/// before any test, allowing tests to execute in parallel without a serializing
/// xUnit collection fixture.
/// </summary>
internal static class DumpGenerationSetup
{
    private static readonly string s_root = RuntimeInformation.ProcessArchitecture == Architecture.X86 ? @"SOFTWARE\WOW6432Node\" : @"SOFTWARE\";
    private static readonly string s_nodePath = s_root + @"Microsoft\Windows NT\CurrentVersion\";
    private static readonly string s_auxiliaryNode = s_nodePath + "MiniDumpAuxiliaryDlls";
    private static readonly string s_knownNode = s_nodePath + "KnownManagedDebuggingDlls";
    private static readonly string s_settingsNode = s_nodePath + "MiniDumpSettings";
    private static readonly string s_disableCheckValue = "DisableAuxProviderSignatureCheck";

    private static HashSet<string> s_paths;

    [ModuleInitializer]
    internal static void Initialize()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        // Create the key for the newer Windows (11 or greater)
        try
        {
            using RegistryKey settingsKey = Registry.LocalMachine.CreateSubKey(s_settingsNode, writable: true);
            settingsKey.SetValue(s_disableCheckValue, 1, RegistryValueKind.DWord);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException)
        {
        }

        // Create a unique list of all the installed test runtime paths
        HashSet<string> paths = new();
        foreach (TestConfiguration config in TestRunConfiguration.Instance.Configurations)
        {
            // Enumerate configs until we see this property
            if (config.AllSettings.TryGetValue("MicrosoftNETCoreAppPath", out string path))
            {
                if (!string.IsNullOrEmpty(path))
                {
                    path = TestConfiguration.MakeCanonicalPath(path);
                    try
                    {
                        foreach (string directory in Directory.GetDirectories(path))
                        {
                            if (Path.GetFileName(directory).StartsWith("10"))
                            {
                                paths.Add(directory);
                            }
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                    }
                    break;
                }
            }
        }

        if (paths.Count > 0)
        {
            // Now try to create the keys for the older Windows versions
            try
            {
                using RegistryKey auxiliaryKey = Registry.LocalMachine.CreateSubKey(s_auxiliaryNode, writable: true);
                using RegistryKey knownKey = Registry.LocalMachine.CreateSubKey(s_knownNode, writable: true);

                foreach (string path in paths)
                {
                    string dacPath = Path.Combine(path, "mscordaccore.dll");
                    string runtimePath = Path.Combine(path, "coreclr.dll");
                    knownKey.SetValue(dacPath, 0, RegistryValueKind.DWord);
                    auxiliaryKey.SetValue(runtimePath, dacPath, RegistryValueKind.String);
                }

                // Save the paths after writing them successfully to registry
                s_paths = paths;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException)
            {
            }
        }

        AppDomain.CurrentDomain.ProcessExit += Cleanup;
    }

    private static void Cleanup(object sender, EventArgs e)
    {
        if (s_paths is not null && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                HashSet<string> paths = s_paths;
                s_paths = null;

                using RegistryKey auxiliaryKey = Registry.LocalMachine.CreateSubKey(s_auxiliaryNode, writable: true);
                using RegistryKey knownKey = Registry.LocalMachine.CreateSubKey(s_knownNode, writable: true);

                foreach (string path in paths)
                {
                    string dacPath = Path.Combine(path, "mscordaccore.dll");
                    string runtimePath = Path.Combine(path, "coreclr.dll");
                    knownKey.DeleteValue(dacPath);
                    auxiliaryKey.DeleteValue(runtimePath);
                }
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}

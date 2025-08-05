// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.TestHelpers;
using Microsoft.Win32;
using Xunit;

public class DumpGenerationFixture : IDisposable
{
    private static readonly string _root = RuntimeInformation.ProcessArchitecture == Architecture.X86 ? @"SOFTWARE\WOW6432Node\" : @"SOFTWARE\";
    private static readonly string _nodePath = _root + @"Microsoft\Windows NT\CurrentVersion\";
    private static readonly string _auxiliaryNode = _nodePath + "MiniDumpAuxiliaryDlls";
    private static readonly string _knownNode = _nodePath + "KnownManagedDebuggingDlls";
    private static readonly string _settingsNode = _nodePath + "MiniDumpSettings";
    private static readonly string _disableCheckValue = "DisableAuxProviderSignatureCheck";

    private HashSet<string> _paths;

    public DumpGenerationFixture()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Create the key for the newer Windows (11 or greater) 
            try
            {
                using RegistryKey settingsKey = Registry.LocalMachine.CreateSubKey(_settingsNode, writable: true);
                settingsKey.SetValue(_disableCheckValue, 1, RegistryValueKind.DWord);
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
                    using RegistryKey auxiliaryKey = Registry.LocalMachine.CreateSubKey(_auxiliaryNode, writable: true);
                    using RegistryKey knownKey = Registry.LocalMachine.CreateSubKey(_knownNode, writable: true);

                    foreach (string path in paths)
                    {
                        string dacPath = Path.Combine(path, "mscordaccore.dll");
                        string runtimePath = Path.Combine(path, "coreclr.dll");
                        knownKey.SetValue(dacPath, 0, RegistryValueKind.DWord);
                        auxiliaryKey.SetValue(runtimePath, dacPath, RegistryValueKind.String);
                    }

                    // Save the paths after writing them successfully to registry
                    _paths = paths;
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException)
                {
                }
            }
        }
    }

    public void Dispose()
    {
        if (_paths is not null && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                IEnumerable<string> paths = _paths;
                _paths = null;

                using RegistryKey auxiliaryKey = Registry.LocalMachine.CreateSubKey(_auxiliaryNode, writable: true);
                using RegistryKey knownKey = Registry.LocalMachine.CreateSubKey(_knownNode, writable: true);

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

[CollectionDefinition("Windows Dump Generation")]
public class DumpGenerationCollection : ICollectionFixture<DumpGenerationFixture>
{
}

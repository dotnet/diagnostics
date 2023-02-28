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

    private HashSet<string> _paths;

    public DumpGenerationFixture() 
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Create a unique list of all the runtime paths used by the tests
            HashSet<string> paths = new();
            foreach (TestConfiguration config in TestRunConfiguration.Instance.Configurations)
            {
                if (config.IsNETCore && config.RuntimeFrameworkVersionMajor >= 8)
                {
                    string path = config.RuntimeSymbolsPath;
                    if (!string.IsNullOrEmpty(path))
                    {
                        paths.Add(path);
                    }
                }
            }

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
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException)
            {
            }
        }
    }
}

[CollectionDefinition("Windows Dump Generation")]
public class DumpGenerationCollection : ICollectionFixture<DumpGenerationFixture>
{
}

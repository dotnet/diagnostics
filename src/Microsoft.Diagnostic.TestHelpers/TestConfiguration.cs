// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using Xunit;
using Xunit.Extensions;

namespace Microsoft.Diagnostic.TestHelpers
{
    /// <summary>
    /// Represents the all the test configurations for a test run.
    /// </summary>
    public class TestRunConfiguration : IDisposable
    {
        public static TestRunConfiguration Instance
        {
            get { return _instance.Value; }
        }

        static Lazy<TestRunConfiguration> _instance = new Lazy<TestRunConfiguration>(() => ParseDefaultConfigFile());

        static TestRunConfiguration ParseDefaultConfigFile()
        {
            string configFilePath = Path.Combine(TestConfiguration.BaseDir, "Debugger.Tests.Config.txt");
            TestRunConfiguration testRunConfig = new TestRunConfiguration();
            testRunConfig.ParseConfigFile(configFilePath);
            return testRunConfig;
        }

        DateTime _timestamp = DateTime.Now;

        public IEnumerable<TestConfiguration> Configurations { get; private set; }

        void ParseConfigFile(string path)
        {
            string nugetPackages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
            if (nugetPackages == null)
            {
                // If not already set, the arcade SDK scripts/build system sets NUGET_PACKAGES 
                // to the UserProfile or HOME nuget cache directories if building locally (for 
                // speed) or to the repo root/.packages in CI builds (to isolate global machine 
                // dependences).
                //
                // This emulates that logic so the VS Test Explorer can still run the tests for
                // config files that don't set the NugetPackagesCacheDir value (like the SOS unit
                // tests).
                string nugetPackagesRoot = null;
                if (OS.Kind == OSKind.Windows)
                {
                    nugetPackagesRoot = Environment.GetEnvironmentVariable("UserProfile");
                }
                else if (OS.Kind == OSKind.Linux || OS.Kind == OSKind.OSX)
                {
                    nugetPackagesRoot = Environment.GetEnvironmentVariable("HOME");
                }
                if (nugetPackagesRoot != null)
                {
                    nugetPackages = Path.Combine(nugetPackagesRoot, ".nuget", "packages");
                }
            }
            // The TargetArchitecture and NuGetPackageCacheDir can still be overridden
            // in a config file. This is just setting the default. The other values can 
            // also // be overriden but it is not recommended.
            Dictionary<string, string> initialConfig = new Dictionary<string, string>
            {
                ["Timestamp"] = GetTimeStampText(),
                ["TempPath"] = Path.GetTempPath(),
                ["WorkingDir"] = GetInitialWorkingDir(),
                ["OS"] = OS.Kind.ToString(),
                ["TargetArchitecture"] = OS.TargetArchitecture.ToString().ToLowerInvariant(),
                ["NuGetPackageCacheDir"] = nugetPackages
            };
            if (OS.Kind == OSKind.Windows)
            {
                initialConfig["WinDir"] = Path.GetFullPath(Environment.GetEnvironmentVariable("WINDIR"));
            }
            IEnumerable<Dictionary<string, string>> configs = ParseConfigFile(path, new Dictionary<string, string>[] { initialConfig });
            Configurations = configs.Select(c => new TestConfiguration(c));
        }

        Dictionary<string, string>[] ParseConfigFile(string path, Dictionary<string, string>[] templates)
        {
            XDocument doc = XDocument.Load(path);
            XElement elem = doc.Root;
            Assert.Equal("Configuration", elem.Name);
            return ParseConfigSettings(templates, elem);
        }

        string GetTimeStampText()
        {
            return _timestamp.ToString("yyyy\\_MM\\_dd\\_hh\\_mm\\_ss\\_ffff");
        }

        string GetInitialWorkingDir()
        {
            return Path.Combine(Path.GetTempPath(), "TestRun_" + GetTimeStampText());
        }

        Dictionary<string, string>[] ParseConfigSettings(Dictionary<string, string>[] templates, XElement node)
        {
            Dictionary<string, string>[] currentTemplates = templates;
            foreach (XElement child in node.Elements())
            {
                currentTemplates = ParseConfigSetting(currentTemplates, child);
            }
            return currentTemplates;
        }

        Dictionary<string, string>[] ParseConfigSetting(Dictionary<string, string>[] templates, XElement node)
        {
            // As long as the templates are added at the end of the list, the "current" 
            // config for this section is the last one in the array.
            Dictionary<string, string> currentTemplate = templates.Last();

            switch (node.Name.LocalName)
            { 
                case "Options":
                    if (EvaluateConditional(currentTemplate, node))
                    {
                        List<Dictionary<string, string>> newTemplates = new List<Dictionary<string, string>>();
                        foreach (XElement optionNode in node.Elements("Option"))
                        {
                            if (EvaluateConditional(currentTemplate, optionNode))
                            {
                                IEnumerable<Dictionary<string, string>> templateCopy = templates.Select(c => new Dictionary<string, string>(c));
                                newTemplates.AddRange(ParseConfigSettings(templateCopy.ToArray(), optionNode));
                            }
                        }
                        if (newTemplates.Count > 0)
                        {
                            return newTemplates.ToArray();
                        }
                    }
                    break;

                case "Import":
                    if (EvaluateConditional(currentTemplate, node))
                    {
                        foreach (XAttribute attr in node.Attributes("ConfigFile"))
                        {
                            string file = ResolveProperties(currentTemplate, attr.Value).Trim();
                            if (!Path.IsPathRooted(file))
                            {
                                file = Path.Combine(TestConfiguration.BaseDir, file);
                            }
                            templates = ParseConfigFile(file, templates);
                        }
                    }
                    break;

                default:
                    foreach (Dictionary<string, string> config in templates)
                    {
                        // This checks the condition on an individual config value
                        if (EvaluateConditional(config, node))
                        {
                            string resolveNodeValue = ResolveProperties(config, node.Value);
                            config[node.Name.LocalName] = resolveNodeValue;
                        }
                    }
                    break;
            }
            return templates;
        }

        bool EvaluateConditional(Dictionary<string, string> config, XElement node)
        {
            foreach (XAttribute attr in node.Attributes("Condition"))
            {
                string conditionText = attr.Value;

                // Check if Exists('<directory or file>')
                const string existsKeyword = "Exists('";
                int existsStartIndex = conditionText.IndexOf(existsKeyword);
                if (existsStartIndex != -1)
                {
                    bool not = (existsStartIndex > 0) && (conditionText[existsStartIndex - 1] == '!');

                    existsStartIndex += existsKeyword.Length;
                    int existsEndIndex = conditionText.IndexOf("')", existsStartIndex);
                    Assert.NotEqual(-1, existsEndIndex);

                    string path = conditionText.Substring(existsStartIndex, existsEndIndex - existsStartIndex);
                    path = Path.GetFullPath(ResolveProperties(config, path));
                    bool exists = Directory.Exists(path) || File.Exists(path);
                    return not ? !exists : exists;
                }
                else
                {
                    // Check if equals and not equals
                    string[] parts = conditionText.Split("==");
                    bool equal;

                    if (parts.Length == 2)
                    {
                        equal = true;
                    }
                    else
                    {
                        parts = conditionText.Split("!=");
                        Assert.Equal(2, parts.Length);
                        equal = false;
                    }
                    // Resolve any config values in the condition
                    string leftValue = ResolveProperties(config, parts[0]).Trim();
                    string rightValue = ResolveProperties(config, parts[1]).Trim();

                    // Now do the simple string comparison of the left/right sides of the condition
                    return equal ? leftValue == rightValue : leftValue != rightValue;
                }
            }
            return true;
        }

        private string ResolveProperties(Dictionary<string, string> config, string rawNodeValue)
        {
            StringBuilder resolvedValue = new StringBuilder();
            for(int i = 0; i < rawNodeValue.Length; )
            {
                int propStartIndex = rawNodeValue.IndexOf("$(", i);
                if (propStartIndex == -1)
                {
                    if (i != rawNodeValue.Length)
                    {
                        resolvedValue.Append(rawNodeValue.Substring(i));
                    }
                    break;
                }
                else
                {
                    int propEndIndex = rawNodeValue.IndexOf(")", propStartIndex+1);
                    Assert.NotEqual(-1, propEndIndex);
                    if (propStartIndex != i)
                    {
                        resolvedValue.Append(rawNodeValue.Substring(i, propStartIndex - i));
                    }
                    // Now resolve the property name from the config dictionary
                    string propertyName = rawNodeValue.Substring(propStartIndex + 2, propEndIndex - propStartIndex - 2);
                    resolvedValue.Append(config.GetValueOrDefault(propertyName, ""));
                    i = propEndIndex + 1;
                }
            }

            return resolvedValue.ToString();
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Represents the current test configuration
    /// </summary>
    public class TestConfiguration
    {
        const string DebugTypeKey = "DebugType";
        const string DebuggeeBuildRootKey = "DebuggeeBuildRoot";

        internal static readonly string BaseDir = Path.GetFullPath(".");

        private Dictionary<string, string> _settings;

        public TestConfiguration()
        {
            _settings = new Dictionary<string, string>();
        }

        public TestConfiguration(Dictionary<string, string> initialSettings)
        {
            _settings = new Dictionary<string, string>(initialSettings);
        }

        public IReadOnlyDictionary<string, string> AllSettings
        {
            get { return _settings; }
        }

        public TestConfiguration CloneWithNewDebugType(string pdbType)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(pdbType));

            var currentSettings = new Dictionary<string, string>(_settings);

            // Set or replace if the pdb debug type
            currentSettings[DebugTypeKey] = pdbType;

            // The debuggee build root must exist. Append the pdb type to make it unique.
            currentSettings[DebuggeeBuildRootKey] = Path.Combine(currentSettings[DebuggeeBuildRootKey], pdbType);

            return new TestConfiguration(currentSettings);
        }

        /// <summary>
        /// The target architecture (x64, x86, arm, arm64) to build and run. If the config
        /// file doesn't have an TargetArchitecture property, then the current running
        /// architecture is used.
        /// </summary>
        public string TargetArchitecture
        {
            get { return GetValue("TargetArchitecture").ToLowerInvariant(); }
        }

        /// <summary>
        /// Built for "Debug" or "Release". Can be null.
        /// </summary>
        public string TargetConfiguration
        {
            get { return GetValue("TargetConfiguration"); }
        }

        /// <summary>
        /// The product "projectk" (.NET Core) or "desktop".
        /// </summary>
        public string TestProduct
        {
            get { return GetValue("TestProduct").ToLowerInvariant(); }
        }

        /// <summary>
        /// Returns true if running on .NET Core (based on TestProduct).
        /// </summary>
        public bool IsNETCore
        {
            get { return TestProduct.Equals("projectk"); }
        }

        /// <summary>
        /// Returns true if running on desktop framework (based on TestProduct).
        /// </summary>
        public bool IsDesktop
        {
            get { return TestProduct.Equals("desktop"); }
        }

        /// <summary>
        /// The test runner script directory 
        /// </summary>
        public string ScriptRootDir
        {
            get { return MakeCanonicalPath(GetValue("ScriptRootDir")); }
        }

        /// <summary>
        /// Working temporary directory.
        /// </summary>
        public string WorkingDir
        {
            get { return MakeCanonicalPath(GetValue("WorkingDir")); }
        }

        /// <summary>
        /// The host program to run a .NET Core or null for desktop/no host.
        /// </summary>
        public string HostExe
        {
            get { return MakeCanonicalExePath(GetValue("HostExe")); }
        }

        /// <summary>
        /// Arguments to the HostExe.
        /// </summary>
        public string HostArgs
        {
            get { return GetValue("HostArgs"); }
        }

        /// <summary>
        /// Environment variables to pass to the target process (via the ProcessRunner).
        /// </summary>
        public string HostEnvVars
        {
            get { return GetValue("HostEnvVars"); }
        }

        /// <summary>
        /// Add the host environment variables to the process runner.
        /// </summary>
        /// <param name="runner">process runner instance</param>
        public void AddHostEnvVars(ProcessRunner runner)
        {
            if (HostEnvVars != null)
            {
                string[] vars = HostEnvVars.Split(';');
                foreach (string var in vars)
                {
                    if (string.IsNullOrEmpty(var))
                    {
                        continue;
                    }
                    string[] parts = var.Split('=');
                    runner = runner.WithEnvironmentVariable(parts[0], parts[1]);
                }
            }
        }

        /// <summary>
        /// The directory to the runtime (coreclr.dll, etc.) symbols
        /// </summary>
        public string RuntimeSymbolsPath
        {
            get { return MakeCanonicalPath(GetValue("RuntimeSymbolsPath")); }
        }

        /// <summary>
        /// How the debuggees are built: "prebuilt" or "cli" (builds the debuggee during the test run with build and cli configuration).
        /// </summary>
        public string DebuggeeBuildProcess
        {
            get { return GetValue("DebuggeeBuildProcess")?.ToLowerInvariant(); }
        }

        /// <summary>
        /// Debuggee sources and template project file will be retrieved from here: <DebuggeeSourceRoot>/<DebuggeeName>/[<DebuggeeName>]
        /// </summary>
        public string DebuggeeSourceRoot
        {
            get { return MakeCanonicalPath(GetValue("DebuggeeSourceRoot")); }
        }

        /// <summary>
        /// Debuggee final sources/project file/binary outputs will be placed here: <DebuggeeBuildRoot>/<DebuggeeName>/
        /// </summary>
        public string DebuggeeBuildRoot
        {
            get { return MakeCanonicalPath(GetValue(DebuggeeBuildRootKey)); }
        }

        /// <summary>
        /// Debuggee native binary dependencies will be retrieved from here.
        /// </summary>
        public string DebuggeeNativeLibRoot
        {
            get { return MakeCanonicalPath(GetValue("DebuggeeNativeLibRoot")); }
        }

        /// <summary>
        /// The version of the Microsoft.NETCore.App package to reference when building the debuggee.
        /// </summary>
        public string BuildProjectMicrosoftNetCoreAppVersion
        {
            get { return GetValue("BuildProjectMicrosoftNetCoreAppVersion"); }
        }

        /// <summary>
        /// The framework type/version used to build the debuggee like "netcoreapp2.0" or "netstandard1.0".
        /// </summary>
        public string BuildProjectFramework
        {
            get { return GetValue("BuildProjectFramework"); }
        }

        /// <summary>
        /// Optional runtime identifier (RID) like "linux-x64" or "win-x86". If set, causes the debuggee to 
        /// be built a as "standalone" dotnet cli project where the runtime is copied to the debuggee build 
        /// root.
        /// </summary>
        public string BuildProjectRuntime
        {
            get { return GetValue("BuildProjectRuntime"); }
        }

        /// <summary>
        /// The version of the Microsoft.NETCore.App package to reference when running the debuggee (i.e. 
        /// using the dotnet cli --fx-version option).
        /// </summary>
        public string RuntimeFrameworkVersion 
        {
            get { return GetValue("RuntimeFrameworkVersion"); }
        }

        /// <summary>
        /// The major portion of the runtime framework version
        /// </summary>
        public int RuntimeFrameworkVersionMajor
        {
            get {
                string version = RuntimeFrameworkVersion;
                if (version != null) {
                    string[] parts = version.Split('.');
                    if (parts.Length > 0) {
                        if (int.TryParse(parts[0], out int major)) {
                            return major;
                        }
                    }
                }
                throw new SkipTestException("RuntimeFrameworkVersion (major) is not valid");
            }
        }

        /// <summary>
        /// The type of PDB: "full" (Windows PDB) or "portable".
        /// </summary>
        public string DebugType
        {
            get { return GetValue(DebugTypeKey); }
        }

        /// <summary>
        /// Either the local path to the dotnet cli to build or the URL of the runtime to download and install.
        /// </summary>
        public string CliPath
        {
            get { return MakeCanonicalPath(GetValue("CliPath")); }
        }

        /// <summary>
        /// The local path to put the downloaded and decompressed runtime.
        /// </summary>
        public string CliCacheRoot
        {
            get { return MakeCanonicalPath(GetValue("CliCacheRoot")); }
        }

        /// <summary>
        /// The version (i.e. 2.0.0) of the dotnet cli to use.
        /// </summary>
        public string CliVersion
        {
            get { return GetValue("CliVersion"); }
        }

        /// <summary>
        /// The directory to cache the nuget packages on restore
        /// </summary>
        public string NuGetPackageCacheDir
        {
            get { return MakeCanonicalPath(GetValue("NuGetPackageCacheDir")); }
        }

        /// <summary>
        /// The nuget package feeds separated by semicolons.
        /// </summary>
        public string NuGetPackageFeeds
        {
            get { return GetValue("NuGetPackageFeeds"); }
        }

        /// <summary>
        /// If true, log the test output, etc. to the console.
        /// </summary>
        public bool LogToConsole
        {
            get { return bool.TryParse(GetValue("LogToConsole"), out bool b) && b; }
        }

        /// <summary>
        /// The directory to put the test logs.
        /// </summary>
        public string LogDirPath
        {
            get { return MakeCanonicalPath(GetValue("LogDir")); }
        }

        /// <summary>
        /// The "ILLink.Tasks" package version to reference or null.
        /// </summary>
        public string LinkerPackageVersion
        {
            get { return GetValue("LinkerPackageVersion"); }
        }

        #region Runtime Features properties

        /// <summary>
        /// Returns true if the "createdump" facility exists.
        /// </summary>
        public bool CreateDumpExists
        {
            get { return OS.Kind == OSKind.Linux && IsNETCore && RuntimeFrameworkVersionMajor > 1; }
        }

        /// <summary>
        /// Returns true if a stack overflow causes dump to be generated with createdump. 3.x has now started to
        /// create dumps on stack overflow.
        /// </summary>
        public bool StackOverflowCreatesDump
        {
            get { return IsNETCore && RuntimeFrameworkVersionMajor >= 3; }
        }

        /// <summary>
        /// Returns true if a stack overflow causes a SIGSEGV exception instead of aborting.
        /// </summary>
        public bool StackOverflowSIGSEGV
        {
            get { return OS.Kind == OSKind.Linux && IsNETCore && RuntimeFrameworkVersionMajor == 1; }
        }

        #endregion

        /// <summary>
        /// Returns the configuration value for the key or null.
        /// </summary>
        /// <param name="key">name of the configuration value</param>
        /// <returns>configuration value or null</returns>
        public string GetValue(string key)
        {
            // unlike dictionary it is OK to ask for non-existant keys
            // if the key doesn't exist the result is null
            _settings.TryGetValue(key, out string settingValue);
            return settingValue;
        }

        public static string MakeCanonicalExePath(string maybeRelativePath)
        {
            if (string.IsNullOrWhiteSpace(maybeRelativePath))
            {
                return null;
            }
            string maybeRelativePathWithExtension = maybeRelativePath;
            if (OS.Kind == OSKind.Windows && !maybeRelativePath.EndsWith(".exe"))
            {
                maybeRelativePathWithExtension = maybeRelativePath + ".exe";
            }
            return MakeCanonicalPath(maybeRelativePathWithExtension);
        }

        public static string MakeCanonicalPath(string maybeRelativePath)
        {
            return MakeCanonicalPath(BaseDir, maybeRelativePath);
        }

        public static string MakeCanonicalPath(string baseDir, string maybeRelativePath)
        {
            if (string.IsNullOrWhiteSpace(maybeRelativePath))
            {
                return null;
            }
            // we will assume any path referencing an http endpoint is canonical already
            if(maybeRelativePath.StartsWith("http:") ||
               maybeRelativePath.StartsWith("https:"))
            {
                return maybeRelativePath;
            }
            string path = Path.IsPathRooted(maybeRelativePath) ? maybeRelativePath : Path.Combine(baseDir, maybeRelativePath);
            path = Path.GetFullPath(path);
            return OS.Kind != OSKind.Windows ? path.Replace('\\', '/') : path;
        }

        public override string ToString()
        {
            return TestProduct + "." + DebuggeeBuildProcess;
        }
    }

    /// <summary>
    /// The OS running
    /// </summary>
    public enum OSKind
    {
        Windows,
        Linux,
        OSX,
        Unknown,
    }

    /// <summary>
    /// The OS specific configuration
    /// </summary>
    public static class OS
    {
        static OS()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Kind = OSKind.Linux;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Kind = OSKind.OSX;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Kind = OSKind.Windows;
            }
            else
            {
                // Default to Unknown
                Kind = OSKind.Unknown;
            }
        }

        /// <summary>
        /// The OS the tests are running.
        /// </summary>
        public static OSKind Kind { get; private set; }

        /// <summary>
        /// The architecture the tests are running.  We are assuming that the test runner, the debugger and the debugger's target are all the same architecture.
        /// </summary>
        public static Architecture TargetArchitecture { get { return RuntimeInformation.ProcessArchitecture; } }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;
using Xunit.Extensions;

namespace Microsoft.Diagnostics.TestHelpers
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
            // also // be overridden but it is not recommended.
            Dictionary<string, string> initialConfig = new Dictionary<string, string>
            {
                ["Timestamp"] = GetTimeStampText(),
                ["TempPath"] = Path.GetTempPath(),
                ["WorkingDir"] = GetInitialWorkingDir(),
                ["OS"] = OS.Kind.ToString(),
                ["IsAlpine"] = OS.IsAlpine.ToString().ToLowerInvariant(),
                ["TargetRid"] = GetRid(),
                ["TargetArchitecture"] = OS.TargetArchitecture.ToString().ToLowerInvariant(),
                ["NuGetPackageCacheDir"] = nugetPackages
            };
            if (OS.Kind == OSKind.Windows)
            {
                initialConfig["WinDir"] = Path.GetFullPath(Environment.GetEnvironmentVariable("WINDIR"));
            }
            IEnumerable<Dictionary<string, string>> configs = ParseConfigFile(path, new Dictionary<string, string>[] { initialConfig });
            Configurations = configs.Select(c => new TestConfiguration(c)).ToList();
        }

        static string GetRid()
        {
            string os = OS.Kind switch
            {
                OSKind.Linux => OS.IsAlpine ? "linux-musl" : "linux",
                OSKind.OSX => "osx",
                OSKind.Windows => "win",
                _ => throw new PlatformNotSupportedException(),
            };
            string architecture = OS.TargetArchitecture.ToString().ToLowerInvariant();
            return $"{os}-{architecture}";
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

        // Currently we only support single function clauses as follows
        //  Exists('<string: file or directory name>')
        //  StartsWith('<string>', '<string: prefix>')
        //  EndsWith('<string>', '<string: postfix>')
        //  Contains('<string>', '<string: substring>')
        //  '<string>' == '<string>'
        //  '<string>' != '<string>'
        // strings support variable embedding with $(<var_name>). e.g Exists('$(PropsFile)')
        bool EvaluateConditional(Dictionary<string, string> config, XElement node)
        {
            void ValidateAndResolveParameters(string funcName, int expectedParamCount, List<string> paramList)
            {
                if (paramList.Count != expectedParamCount)
                {
                    throw new InvalidDataException($"Expected {expectedParamCount} arguments for {funcName} in condition");
                }

                for (int i = 0; i < paramList.Count; i++)
                {
                    paramList[i] = ResolveProperties(config, paramList[i]);
                }
            }

            foreach (XAttribute attr in node.Attributes("Condition"))
            {
                string conditionText = attr.Value.Trim();
                bool isNegative = conditionText.Length > 0 && conditionText[0] == '!';

                // Check if Exists('<directory or file>')
                const string existsKeyword = "Exists";
                if (TryGetParametersForFunction(conditionText, existsKeyword, out List<string> paramList))
                {
                    ValidateAndResolveParameters(existsKeyword, 1, paramList);
                    bool exists = Directory.Exists(paramList[0]) || File.Exists(paramList[0]);
                    return isNegative ? !exists : exists;
                }

                // Check if StartsWith('string', 'prefix')
                const string startsWithKeyword = "StartsWith";
                if (TryGetParametersForFunction(conditionText, startsWithKeyword, out paramList))
                {
                    ValidateAndResolveParameters(startsWithKeyword, 2, paramList);
                    bool isPrefix = paramList[0].StartsWith(paramList[1]);
                    return isNegative ? !isPrefix : isPrefix;
                }

                // Check if EndsWith('string', 'postfix')
                const string endsWithKeyword = "EndsWith";
                if (TryGetParametersForFunction(conditionText, endsWithKeyword, out paramList))
                {
                    ValidateAndResolveParameters(endsWithKeyword, 2, paramList);
                    bool isPostfix = paramList[0].EndsWith(paramList[1]);
                    return isNegative ? !isPostfix : isPostfix;
                }

                // Check if Contains('string', 'substring')
                const string containsKeyword = "Contains";
                if (TryGetParametersForFunction(conditionText, containsKeyword, out paramList))
                {
                    ValidateAndResolveParameters(containsKeyword, 2, paramList);
                    bool isInString = paramList[0].Contains(paramList[1]);
                    return isNegative ? !isInString : isInString;
                }

                // Check if equals and not equals
                bool isEquals = conditionText.Contains("==");
                bool isDifferent = conditionText.Contains("!=");
                
                if (isEquals == isDifferent) 
                {
                    throw new InvalidDataException($"Unknown condition type in {conditionText}. See TestRunConfiguration.EvaluateConditional for supported values.");
                }

                string[] parts = isEquals ? conditionText.Split("==") : conditionText.Split("!=");

                // Resolve any config values in the condition
                string leftValue = ResolveProperties(config, parts[0]).Trim();
                string rightValue = ResolveProperties(config, parts[1]).Trim();

                // Now do the simple string comparison of the left/right sides of the condition
                return isEquals ? leftValue == rightValue : leftValue != rightValue;
            }
            return true;
        }

        private bool TryGetParametersForFunction(string expression, string targetFunctionName, out List<string> exprParams)
        {
            int functionKeyworkIndex = expression.IndexOf($"{targetFunctionName}(");
            if (functionKeyworkIndex == -1) {
                exprParams = null;
                return false;
            }

            if ((functionKeyworkIndex != 0 
                    && functionKeyworkIndex == 1 && expression[0] != '!')
                || functionKeyworkIndex > 1
                || !expression.EndsWith(')'))
            {
                throw new InvalidDataException($"Condition {expression} malformed. Currently only single-function conditions are supported.");
            }

            exprParams = new List<string>();
            bool isWithinString = false;
            bool expectDelimiter = false;
            int curParsingIndex = functionKeyworkIndex + targetFunctionName.Length + 1;
            StringBuilder resolvedValue = new StringBuilder();

            // Account for the trailing parenthesis.
            while(curParsingIndex + 1 < expression.Length)
            {
                char currentChar = expression[curParsingIndex];
                // toggle string nesting on ', except if scaped
                if (currentChar == '\'' && !(curParsingIndex > 0 && expression[curParsingIndex - 1] == '\\'))
                {
                    if (isWithinString)
                    {
                        exprParams.Add(resolvedValue.ToString());
                        resolvedValue.Clear();
                        expectDelimiter = true;
                    }

                    isWithinString = !isWithinString;
                }
                else if (isWithinString)
                {
                    resolvedValue.Append(currentChar);
                }
                else if (currentChar == ',')
                {
                    if (!expectDelimiter)
                    {
                        throw new InvalidDataException($"Unexpected comma found within {expression}");
                    }
                    expectDelimiter = false;
                }
                else if (!Char.IsWhiteSpace(currentChar))
                {
                    throw new InvalidDataException($"Non whitespace, non comma value found outside of string within: {expression}");
                }
                curParsingIndex++;
            }

            if (isWithinString) {
                throw new InvalidDataException($"Non-terminated string detected within {expression}");
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
        
        public static string BaseDir { get; set; } = Path.GetFullPath(".");

        private static readonly Regex versionRegex = new Regex(@"^(\d+\.\d+\.\d+)(-.*)?", RegexOptions.Compiled);
        
        private ReadOnlyDictionary<string, string> _settings;
        private readonly string _configStringView;
        private readonly string _truncatedRuntimeFrameworkVersion;

        public TestConfiguration()
        {
            _settings = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
            _truncatedRuntimeFrameworkVersion = null;
            _configStringView = string.Empty;
        }

        public TestConfiguration(Dictionary<string, string> initialSettings)
        {
            _settings = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(initialSettings));
            _truncatedRuntimeFrameworkVersion = GetTruncatedRuntimeFrameworkVersion();
            _configStringView = GetStringViewWithVersion(RuntimeFrameworkVersion); 
        }

        public string Serialize()
        {
            List<XElement> nodes = new();
            foreach (KeyValuePair<string, string> keyvalue in _settings)
            {
                nodes.Add(new XElement(keyvalue.Key, keyvalue.Value));
            }
            XElement root = new("Configuration", nodes.ToArray());
            TextWriter writer = new StringWriter();
            root.Save(writer);
            return writer.ToString();
        }

        public static TestConfiguration Deserialize(string xml)
        {
            XElement root = XElement.Parse(xml);
            Dictionary<string, string> settings = new();
            foreach (XElement child in root.Elements())
            {
                settings.Add(child.Name.LocalName, child.Value);
            }
            return new TestConfiguration(settings);
        }

        private string GetTruncatedRuntimeFrameworkVersion()
        {
            string version = RuntimeFrameworkVersion;
            if (string.IsNullOrEmpty(version))
            {
                return null;
            }

            Match matchingVer = versionRegex.Match(version);
            if (!matchingVer.Success)
            {
                throw new InvalidDataException($"{version} is not a valid version string according to SemVer.");
            }

            return matchingVer.Groups[1].Value;
        }

        private string GetStringViewWithVersion(string version)
        {
            var sb = new StringBuilder();
            sb.Append(TestProduct ?? "");
            string debuggeeBuildProcess = DebuggeeBuildProcess;
            if (!string.IsNullOrEmpty(debuggeeBuildProcess))
            {
                sb.Append(".");
                sb.Append(debuggeeBuildProcess);
            }
            if (PublishSingleFile)
            {
                sb.Append(".singlefile");
            }
            if (!string.IsNullOrEmpty(version))
            {
                sb.Append(".");
                sb.Append(version);
            }
            return sb.ToString();
        }

        internal string GetLogSuffix()
        {
            string version = RuntimeFrameworkVersion;

            // The log name can't contain wild cards, which are used in some testing scenarios.
            // TODO: The better solution would be to sanitize the file name properly, in case
            // there's a key being used that contains a character that is not a valid file
            // name charater.
            if (!string.IsNullOrEmpty(version) && version.Contains('*'))
            {
                version = _truncatedRuntimeFrameworkVersion;
            }

            return GetStringViewWithVersion(version);
        }

        public IReadOnlyDictionary<string, string> AllSettings
        {
            get { return _settings; }
        }

        /// <summary>
        /// Creates a new test config with the new PDB type (full, portable or embedded)
        /// </summary>
        /// <param name="pdbType">new pdb type</param>
        /// <returns>new test config</returns>
        public TestConfiguration CloneWithNewDebugType(string pdbType)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(pdbType));

            var currentSettings = new Dictionary<string, string>(_settings) {

                // Set or replace if the pdb debug type
                [DebugTypeKey] = pdbType,

                // The debuggee build root must exist. Append the pdb type to make it unique.
                [DebuggeeBuildRootKey] = Path.Combine(_settings[DebuggeeBuildRootKey], pdbType)
            };
            return new TestConfiguration(currentSettings);
        }

        /// <summary>
        /// The target architecture (x64, x86, arm, arm64) to build and run. If the config
        /// file doesn't have an TargetArchitecture property, then the current running
        /// architecture is used.
        /// </summary>
        public string TargetArchitecture
        {
            get { return GetValue("TargetArchitecture")?.ToLowerInvariant(); }
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
            get { return GetValue("TestProduct")?.ToLowerInvariant(); }
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
        /// The framework type/version used to build the debuggee like "netcoreapp3.1" or "netstandard2.0".
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
        /// Returns "true" if build/run this cli debuggee as a single-file app
        /// </summary>
        public bool PublishSingleFile
        {
            get { return string.Equals(GetValue("PublishSingleFile"), "true", StringComparison.InvariantCultureIgnoreCase); }
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
        /// <exception cref="SkipTestException">the RuntimeFrameworkVersion property doesn't exist</exception>
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
            get { return string.Equals(GetValue("LogToConsole"), "true", StringComparison.InvariantCultureIgnoreCase); }
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

        /// <summary>
        /// The root of the dotnet install to use to run the test (i.e. $(RepoRootDir)/.dotnet-test)
        /// </summary>
        public string DotNetRoot
        {
            get
            {
                string dotnetRoot = GetValue("DotNetRoot");
                return MakeCanonicalPath(dotnetRoot);
            }
        }

        #region Runtime Features properties

        /// <summary>
        /// Returns true if the "createdump" facility exists.
        /// </summary>
        public bool CreateDumpExists
        {
            get
            {
                return OS.Kind == OSKind.Linux && IsNETCore && RuntimeFrameworkVersionMajor >= 2 ||
                       OS.Kind == OSKind.OSX && IsNETCore && RuntimeFrameworkVersionMajor >= 5 ||
                       OS.Kind == OSKind.Windows && IsNETCore && RuntimeFrameworkVersionMajor >= 5;
            }
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
            // unlike dictionary it is OK to ask for non-existent keys
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
            return _configStringView;
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
            if (OS.Kind == OSKind.Linux)
            {
                try
                {
                    string ostype = File.ReadAllText("/etc/os-release");
                    IsAlpine = ostype.Contains("ID=alpine");
                }
                catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException || ex is IOException)
                {
                }
            }
        }

        /// <summary>
        /// The OS the tests are running.
        /// </summary>
        public static OSKind Kind { get; private set; }

        /// <summary>
        /// Returns true if Alpine Linux distro
        /// </summary>
        public static bool IsAlpine { get; private set; }

        /// <summary>
        /// The architecture the tests are running.  We are assuming that the test runner, the debugger and the debugger's target are all the same architecture.
        /// </summary>
        public static Architecture TargetArchitecture { get { return RuntimeInformation.ProcessArchitecture; } }
    }
}

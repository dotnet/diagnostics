using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Debugger.Tests
{
    public class TestRunConfiguration : IDisposable
    {
        public static TestRunConfiguration Instance
        {
            get { return _instance.Value; }
        }

        static Lazy<TestRunConfiguration> _instance = new Lazy<TestRunConfiguration>(() => ParseDefaultConfigFile());
        static string BaseDir = Path.GetFullPath(".");

        static TestRunConfiguration ParseDefaultConfigFile()
        {
            string configFilePath = Path.Combine(BaseDir, "Debugger.Tests.Config.txt");
            TestRunConfiguration testRunConfig = new TestRunConfiguration();
            testRunConfig.ParseConfigFile(configFilePath);
            return testRunConfig;
        }

        DateTime _timestamp = DateTime.Now;

        public TestConfiguration[] Configurations { get; private set; }

        void ParseConfigFile(string path)
        {
            XDocument doc = XDocument.Load(path);
            XElement elem = doc.Root;
            Assert.Equal("Configuration", elem.Name);
            Dictionary<string,string> initialConfig = new Dictionary<string, string>();
            initialConfig["Timestamp"] = GetTimeStampText();
            initialConfig["TempPath"] = Path.GetTempPath();
            initialConfig["WorkingDir"] = GetInitialWorkingDir();
           
            string SkipMdbgStep = Environment.GetEnvironmentVariable("SkipMdbgStep");
            initialConfig["SkipMdbgStep"] = SkipMdbgStep == null ? "false": SkipMdbgStep.ToLowerInvariant();

            Dictionary<string, string>[] configs = ParseConfigSettings(new Dictionary<string, string>[] { initialConfig }, elem);
            Configurations = configs.Select(c => new TestConfiguration(c)).ToArray();
        }

        string GetTimeStampText()
        {
            return _timestamp.ToString("yyyy\\_MM\\_dd\\_hh\\_mm\\_ss\\_ffff");
        }

        string GetInitialWorkingDir()
        {
            return Path.Combine(Path.GetTempPath(), "TestRun_" + GetTimeStampText());
        }

        Dictionary<string,string>[] ParseConfigSettings(Dictionary<string,string>[] templates, XElement node)
        {
            Dictionary<string, string>[] curTemplates = templates;
            foreach(XElement child in node.Elements())
            {
                curTemplates = ParseConfigSetting(curTemplates, child);
            }
            return curTemplates;
        }

        Dictionary<string, string>[] ParseConfigSetting(Dictionary<string, string>[] templates, XElement node)
        {
            if (node.Name == "Options")
            {
                List<Dictionary<string, string>> newTemplates = new List<Dictionary<string, string>>();
                foreach (XElement optionNode in node.Elements("Option"))
                {
                    Dictionary<string, string>[] templateCopy = templates.Select(c => new Dictionary<string,string>(c)).ToArray();
                    newTemplates.AddRange(ParseConfigSettings(templateCopy, optionNode));
                }
                return newTemplates.ToArray();
            }
            else
            {
                foreach(Dictionary<string, string> config in templates)
                {
                    string resolveNodeValue = ResolveProperties(config, node.Value);
                    config[node.Name.LocalName] = resolveNodeValue;
                }
                return templates;
            }
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
                    resolvedValue.Append(ResolveProperty(config, rawNodeValue.Substring(propStartIndex+2, propEndIndex - propStartIndex-2)));
                    i = propEndIndex + 1;
                }
            }

            return resolvedValue.ToString();
        }

        private string ResolveProperty(Dictionary<string, string> config, string propName)
        {
            if (propName.Equals("WinDir", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(Environment.ExpandEnvironmentVariables("%windir%"));
            }
            string val = config[propName];
            return val == null ? "" : val; 
        }

        public void Dispose()
        {
        }
    }

    
    public class TestConfiguration
    {
        const string DebugTypeKey = "DebugType";
        const string DebuggeeBuildRootKey = "DebuggeeBuildRoot";

        static string BaseDir = Path.GetFullPath(".");

        private Dictionary<string, string> _settings;

        public TestConfiguration()
        {
            _settings = new Dictionary<string, string>();
        }

        public TestConfiguration(Dictionary<string,string> initialSettings)
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

            var currentSettings = new Dictionary<string,string>(_settings);

            // Set or replace if the pdb debug type
            currentSettings[DebugTypeKey] = pdbType;

            // The debuggee build root must exist. Append the pdb type to make it unique.
            currentSettings[DebuggeeBuildRootKey] = Path.Combine(currentSettings[DebuggeeBuildRootKey], pdbType);

            return new TestConfiguration(currentSettings);
        }

        public string ScriptRootDir
        {
            get
            {
                return MakeCanonicalPath(Get("ScriptRootDir"));
            }
        }

        public string MDbgDir
        {
            get
            {
                return MakeCanonicalPath(Get("MDbgDir"));
            }
        }

        public string OrangeDir
        {
            get
            {
                return MakeCanonicalPath(Get("OrangeDir"));
            }
        }

        public string TestProduct
        {
            get
            {
                return Get("TestProduct").ToLowerInvariant();
            }
        }

        public string HostExe
        {
            get
            {
                return MakeCanonicalExePath(Get("HostExe"));
            }
        }

        public string HostArgs
        {
            get
            {
                return Get("HostArgs");
            }
        }

        public string HostEnvVars
        {
            get
            {
                return Get("HostEnvVars");
            }
        }

        public string RuntimeSymbolsPath
        {
            get
            {
                return MakeCanonicalPath(Get("RuntimeSymbolsPath"));
            }
        }

        public string DbgShim
        {
            get
            {
                return MakeCanonicalPath(Get("DbgShim"));
            }
        }

        public string DebuggeeRootDir
        {
            get
            {
                return MakeCanonicalPath(Get("DebuggeeRootDir"));
            }
        }

        public string AdditionalMDbgStartupCommands
        {
            get
            {
                return Get("AdditionalMDbgStartupCommands");
            }
        }

        public string DebuggeeDumpInputRootDir
        {
            get
            {
                return MakeCanonicalPath(Get("DebuggeeDumpInputRootDir"));
            }
        }

        public string DebuggeeDumpOutputRootDir
        {
            get
            {
                return MakeCanonicalPath(Get("DebuggeeDumpOutputRootDir"));
            }
        }

        public string WorkingDir
        {
            get
            {
                return MakeCanonicalPath(Get("WorkingDir"));
            }
        }

        public string DebuggeeBuildProcess
        {
            get
            {
                string debuggeeBuildProcess = Get("DebuggeeBuildProcess");
                return debuggeeBuildProcess == null ? null : debuggeeBuildProcess.ToLowerInvariant();
            }
        }

        public string DebuggeeSourceRoot
        {
            get
            {
                return MakeCanonicalPath(Get("DebuggeeSourceRoot"));
            }
        }

        public string DebuggeeBuildRoot
        {
            get
            {
                return MakeCanonicalPath(Get(DebuggeeBuildRootKey));
            }
        }

        public string DebuggeeNativeLibRoot
        {
            get
            {
                return MakeCanonicalPath(Get("DebuggeeNativeLibRoot"));
            }
        }

        public string BuildProjectMicrosoftNetCoreAppVersion
        {
            get
            {
                return Get("BuildProjectMicrosoftNetCoreAppVersion");
            }
        }

        public string BuildProjectFramework
        {
            get
            {
                return Get("BuildProjectFramework");
            }
        }

        public string BuildProjectRuntime
        {
            get
            {
                return Get("BuildProjectRuntime");
            }
        }

        public string DebugType
        {
            get
            {
                return Get(DebugTypeKey);
            }
        }

        public string CliPath
        {
            get
            {
                return MakeCanonicalPath(Get("CliPath"));
            }
        }

        public string CliCacheRoot
        {
            get
            {
                return MakeCanonicalPath(Get("CliCacheRoot"));
            }
        }

        public string CliVersion
        {
            get
            {
                return Get("CliVersion");
            }
        }

        public string NuGetPackageCacheDir
        {
            get
            {
                return MakeCanonicalPath(Get("NuGetPackageCacheDir"));
            }
        }

        public string NuGetPackageFeeds
        {
            get
            {
                return Get("NuGetPackageFeeds");
            }
        }

        public string TestRoot
        {
            get
            {
                return MakeCanonicalPath(Get("TestRoot"));
            }
        }

        public string CDBPath
        {
            get
            {
                return MakeCanonicalExePath(Get("CDBPath"));
            }
        }

        public string LLDBPath
        {
            get
            {
                return MakeCanonicalPath(Get("LLDBPath"));
            }
        }

        public string LLDBHelperScript
        {
            get
            {
                return MakeCanonicalPath(Get("LLDBHelperScript"));
            }
        }

        public string GDBPath
        {
            get
            {
                return MakeCanonicalPath(Get("GDBPath"));
            }
        }

        public string SOSPath
        {
            get
            {
                return MakeCanonicalPath(Get("SOSPath"));
            }
        }

        public string TargetArchitecture
        {
            get
            {
                return Get("TargetArchitecture").ToLowerInvariant();
            }
        }
        public bool SkipMdbgStep
        {
            get
            {
                return  Get("SkipMdbgStep") == "true";
            }
        }

        public string MDbgExe
        {
            get { return Path.Combine(MDbgDir, "Mdbg.exe"); }
        }

        public string MDbgExtensionDir
        {
            get { return MDbgDir; }
        }

        public string OrangeExe
        {
            get { return Path.Combine(OrangeDir, "Orange.exe"); }
        }

        public bool WaitForDebuggerAttach
        {
            get
            {
                bool b;
                return bool.TryParse(Get("WaitForDebuggerAttach"), out b) && b;
            }
        }

        public bool LogToConsole
        {
            get
            {
                bool b;
                return bool.TryParse(Get("LogToConsole"), out b) && b;
            }
        }

        public string LogDirPath
        {
            get
            {
                return MakeCanonicalPath(Get("LogDir"));
            }
        }

        public string LinkerPackageVersion
        {
            get
            {
                return Get("LinkerPackageVersion");
            }
        }

        private string Get(string key)
        {
            // unlike dictionary it is OK to ask for non-existant keys
            // if the key doesn't exist the result is null
            string settingValue = null;
            _settings.TryGetValue(key, out settingValue);
            return settingValue;
        }

        private string MakeCanonicalExePath(string maybeRelativePath)
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

        private string MakeCanonicalPath(string maybeRelativePath)
        {
            return MakeCanonicalPath(BaseDir, maybeRelativePath);
        }

        private string MakeCanonicalPath(string baseDir, string maybeRelativePath)
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

    public enum OSKind
    {
        Windows,
        Linux,
        OSX,
        FreeBSD,
        Unknown,
    }

    public static class OS
    {
        private static OSKind _kind;

        static OS()
        {
#if CORE_CLR // Only core build can run on different OSes
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                _kind = OSKind.Linux;
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                _kind = OSKind.OSX;
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _kind = OSKind.Windows;
            }
            else
            {
                // Default to Unknown
                _kind = OSKind.Unknown;
            }

#else   // For everything else there's Windows 
            _kind = OSKind.Windows; 
#endif
        }

        public static OSKind Kind
        {
            get
            {
                return _kind;
            }
        }
    }
}

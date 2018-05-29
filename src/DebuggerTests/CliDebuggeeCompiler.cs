using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Debugger.Tests.Build
{
    /// <summary>
    /// This compiler acquires the CLI tools and uses them to build and optionally link debuggees via dotnet publish.
    /// </summary>
    public class CliDebuggeeCompiler : BaseDebuggeeCompiler
    {
        /// <summary>
        /// Creates a new CliDebuggeeCompiler. This compiler acquires the CLI tools and uses them to build and optionally link debuggees via dotnet publish.
		/// <param name="config">
		///   LinkerPackageVersion   If set, this version of the linker package will be used to link the debuggee during publish.
		/// </param>
        /// </summary>
        public CliDebuggeeCompiler(TestConfiguration config, string debuggeeName) : base(config, debuggeeName) {}

        private static Dictionary<string,string> GetBuildProperties(TestConfiguration config, string runtime)
        {
            Dictionary<string, string> buildProperties = new Dictionary<string, string>();
            buildProperties.Add("RuntimeFrameworkVersion", config.BuildProjectMicrosoftNetCoreAppVersion);
            buildProperties.Add("RuntimeIdentifier", runtime);
            string debugType = config.DebugType;
            if (debugType == null)
            {
                // The default PDB type is portable
                debugType = "portable";
            }
            buildProperties.Add("DebugType", debugType);
            return buildProperties;
        }

        protected override string GetFramework(TestConfiguration config)
        {
            return config.BuildProjectFramework ?? "netcoreapp2.0";
        }

        protected override string GetDebuggeeBinaryDirPath(string debuggeeProjectDirPath, string framework, string runtime)
        {
            string debuggeeBinaryDirPath = base.GetDebuggeeBinaryDirPath(debuggeeProjectDirPath, framework, runtime);
            debuggeeBinaryDirPath = Path.Combine(debuggeeBinaryDirPath, "publish");
            return debuggeeBinaryDirPath;
        }

        public override DotNetBuildDebuggeeTestStep ConfigureDotNetBuildDebuggeeTask(TestConfiguration config, string dotNetPath, string cliToolsVersion, string debuggeeName)
        {
            string runtime = GetRuntime(config);
            string initialSourceDirPath = GetInitialSourceDirPath(config, debuggeeName);
            string dotNetRootBuildDirPath = GetDotNetRootBuildDirPath(config);
            string debuggeeSolutionDirPath = GetDebuggeeSolutionDirPath(dotNetRootBuildDirPath, debuggeeName);
            string debuggeeProjectDirPath = GetDebuggeeProjectDirPath(debuggeeSolutionDirPath, initialSourceDirPath, debuggeeName);
            string debuggeeBinaryDirPath = GetDebuggeeBinaryDirPath(debuggeeProjectDirPath, GetFramework(config), runtime);
            string debuggeeBinaryDllPath = GetDebuggeeBinaryDllPath(debuggeeBinaryDirPath, debuggeeName);
            string debuggeeBinaryExePath = GetDebuggeeBinaryExePath(debuggeeBinaryDirPath, debuggeeName);
            return new CsprojBuildDebuggeeTestStep(dotNetPath,
                                               initialSourceDirPath,
                                               GetDebuggeeNativeLibDirPath(config, debuggeeName),
                                               GetBuildProperties(config, runtime),
                                               runtime,
                                               config.LinkerPackageVersion,
                                               debuggeeName,
                                               debuggeeSolutionDirPath,
                                               debuggeeProjectDirPath,
                                               debuggeeBinaryDirPath,
                                               debuggeeBinaryDllPath,
                                               debuggeeBinaryExePath,
                                               config.NuGetPackageCacheDir,
                                               GetNugetFeeds(config),
                                               GetLogPath(config, debuggeeName));
        }
    }
}

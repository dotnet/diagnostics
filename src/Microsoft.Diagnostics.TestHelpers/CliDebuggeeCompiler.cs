// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Microsoft.Diagnostics.TestHelpers
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

        private static Dictionary<string,string> GetBuildProperties(TestConfiguration config, string runtimeIdentifier)
        {
            Dictionary<string, string> buildProperties = new Dictionary<string, string>();
            string buildProjectMicrosoftNetCoreAppVersion = config.BuildProjectMicrosoftNetCoreAppVersion;
            if (!string.IsNullOrEmpty(buildProjectMicrosoftNetCoreAppVersion))
            {
                buildProperties.Add("RuntimeFrameworkVersion", buildProjectMicrosoftNetCoreAppVersion);
            }
            buildProperties.Add("BuildProjectFramework", config.BuildProjectFramework);
            if (runtimeIdentifier != null)
            {
                buildProperties.Add("RuntimeIdentifier", runtimeIdentifier);
            }
            if (config.PublishSingleFile)
            {
                Debug.Assert(runtimeIdentifier != null);
                buildProperties.Add("PublishSingleFile", "true");
                buildProperties.Add("SelfContained", "true");
            }
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
            return config.BuildProjectFramework ?? "netcoreapp3.1";
        }

        protected override string GetDebuggeeBinaryDirPath(string debuggeeProjectDirPath, string framework, string runtime)
        {
            string debuggeeBinaryDirPath = base.GetDebuggeeBinaryDirPath(debuggeeProjectDirPath, framework, runtime);
            debuggeeBinaryDirPath = Path.Combine(debuggeeBinaryDirPath, "publish");
            return debuggeeBinaryDirPath;
        }

        public override DotNetBuildDebuggeeTestStep ConfigureDotNetBuildDebuggeeTask(TestConfiguration config, string dotNetPath, string cliToolsVersion, string debuggeeName)
        {
            string runtimeIdentifier = GetRuntime(config);
            string framework = GetFramework(config);
            string initialSourceDirPath = GetInitialSourceDirPath(config, debuggeeName);
            string dotNetRootBuildDirPath = GetDotNetRootBuildDirPath(config);
            string debuggeeSolutionDirPath = GetDebuggeeSolutionDirPath(dotNetRootBuildDirPath, debuggeeName);
            string debuggeeProjectDirPath = GetDebuggeeProjectDirPath(debuggeeSolutionDirPath, initialSourceDirPath, debuggeeName);
            string debuggeeBinaryDirPath = GetDebuggeeBinaryDirPath(debuggeeProjectDirPath, framework, runtimeIdentifier);
            string debuggeeBinaryDllPath = GetDebuggeeBinaryDllPath(config, debuggeeBinaryDirPath, debuggeeName);
            string debuggeeBinaryExePath = GetDebuggeeBinaryExePath(config, debuggeeBinaryDirPath, debuggeeName);
            string logPath = GetLogPath(config, framework, runtimeIdentifier, debuggeeName);
            return new CsprojBuildDebuggeeTestStep(dotNetPath,
                                               initialSourceDirPath,
                                               GetDebuggeeNativeLibDirPath(config, debuggeeName),
                                               GetBuildProperties(config, runtimeIdentifier),
                                               runtimeIdentifier,
                                               config.BuildProjectFramework,
                                               config.LinkerPackageVersion,
                                               debuggeeName,
                                               debuggeeSolutionDirPath,
                                               debuggeeProjectDirPath,
                                               debuggeeBinaryDirPath,
                                               debuggeeBinaryDllPath,
                                               debuggeeBinaryExePath,
                                               config.NuGetPackageCacheDir,
                                               GetNugetFeeds(config),
                                               logPath);
        }
    }
}

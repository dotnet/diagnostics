// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace Microsoft.Diagnostics.TestHelpers
{
    /// <summary>
    /// This compiler acquires the CLI tools and uses them to build debuggees.
    /// </summary>
    /// <remarks>
    /// The build process consists of the following steps:
    ///   1. Acquire the CLI tools from the CliPath. This generally involves downloading them from the web and unpacking them.
    ///   2. Create a source directory with the conventions that dotnet expects by copying it from the DebuggeeSourceRoot. Any project.template.json
    ///      file that is found will be specialized by replacing macros with specific contents. This lets us decide the runtime and dependency versions
    ///      at test execution time.
    ///   3. Run dotnet restore in the newly created source directory
    ///   4. Run dotnet build in the same directory
    ///   5. Rename the built debuggee dll to an exe (so that it conforms to some historical conventions our tests expect)
    ///   6. Copy any native dependencies from the DebuggeeNativeLibRoot to the debuggee output directory
    /// </remarks>
    public abstract class BaseDebuggeeCompiler : IDebuggeeCompiler
    {
        private AcquireDotNetTestStep _acquireTask;
        private DotNetBuildDebuggeeTestStep _buildDebuggeeTask;

        /// <summary>
        /// Creates a new BaseDebuggeeCompiler. This compiler acquires the CLI tools and uses them to build debuggees via dotnet build.
        /// </summary>
        /// <param name="config">
        /// The test configuration that will be used to configure the build. The following configuration options should be set in the config:
        ///   CliPath                                  The location to get the CLI tools from, either as a .zip/.tar.gz at a web endpoint, a .zip/.tar.gz
        ///                                            at a local filesystem path, or the dotnet binary at a local filesystem path
        ///   WorkingDir                               Temporary storage for CLI tools compressed archives downloaded from the internet will be stored here
        ///   CliCacheRoot                             The final CLI tools will be expanded and cached here
        ///   DebuggeeSourceRoot                       Debuggee sources and template project file will be retrieved from here
        ///   DebuggeeNativeLibRoot                    Debuggee native binary dependencies will be retrieved from here
        ///   DebuggeeBuildRoot                        Debuggee final sources/project file/binary outputs will be placed here
        ///   BuildProjectRuntime                      The runtime moniker to be built
        ///   BuildProjectMicrosoftNETCoreAppVersion   The nuget package version of Microsoft.NETCore.App package to build against for debuggees that references this library
        ///   NugetPackageCacheDir                     The directory where NuGet packages are cached during restore
        ///   NugetFeeds                               The set of nuget feeds that are used to search for packages during restore
        /// </param>
        /// <param name="debuggeeName">
        ///   The name of the debuggee to be built, from which various build file paths are constructed. Before build it is assumed that:
        ///     Debuggee sources are located at               config.DebuggeeSourceRoot/debuggeeName/
        ///     Debuggee native dependencies are located at   config.DebuggeeNativeLibRoot/debuggeeName/
        ///
        ///   After the build:
        ///     Debuggee build outputs will be created at     config.DebuggeeNativeLibRoot/debuggeeName/
        ///     A log of the build is stored at               config.DebuggeeNativeLibRoot/debuggeeName.txt
        /// </param>
        public BaseDebuggeeCompiler(TestConfiguration config, string debuggeeName)
        {
            _acquireTask = ConfigureAcquireDotNetTask(config);
            _buildDebuggeeTask = ConfigureDotNetBuildDebuggeeTask(config, _acquireTask.LocalDotNetPath, config.CliVersion, debuggeeName);
        }

        async public Task<DebuggeeConfiguration> Execute(ITestOutputHelper output)
        {
            if (_acquireTask.AnyWorkToDo)
            {
                await _acquireTask.Execute(output);
            }
            await _buildDebuggeeTask.Execute(output);
            return new DebuggeeConfiguration(_buildDebuggeeTask.DebuggeeProjectDirPath,
                                             _buildDebuggeeTask.DebuggeeBinaryDirPath,
                                             _buildDebuggeeTask.DebuggeeBinaryExePath ?? _buildDebuggeeTask.DebuggeeBinaryDllPath);
        }

        public static AcquireDotNetTestStep ConfigureAcquireDotNetTask(TestConfiguration config)
        {
            string remoteCliZipPath = null;
            string localCliZipPath = null;
            string localCliTarPath = null;
            string localCliExpandedDirPath = null;

            string dotNetPath = config.CliPath;
            if (dotNetPath.StartsWith("http:") || dotNetPath.StartsWith("https:"))
            {
                remoteCliZipPath = dotNetPath;
                dotNetPath = Path.Combine(config.WorkingDir, "dotnet_zip", Path.GetFileName(remoteCliZipPath));
            }
            if (dotNetPath.EndsWith(".zip") || dotNetPath.EndsWith(".tar.gz"))
            {
                localCliZipPath = dotNetPath;
                string cliVersionDirName;
                if (dotNetPath.EndsWith(".tar.gz"))
                {
                    localCliTarPath = localCliZipPath.Substring(0, dotNetPath.Length - 3);
                    cliVersionDirName = Path.GetFileNameWithoutExtension(localCliTarPath);
                }
                else
                {
                    cliVersionDirName = Path.GetFileNameWithoutExtension(localCliZipPath);
                }

                localCliExpandedDirPath = Path.Combine(config.CliCacheRoot, cliVersionDirName);
                dotNetPath = Path.Combine(localCliExpandedDirPath, OS.Kind == OSKind.Windows ? "dotnet.exe" : "dotnet");
            }
            string acquireLogDir = Path.GetDirectoryName(Path.GetDirectoryName(dotNetPath));
            string acquireLogPath = Path.Combine(acquireLogDir, Path.GetDirectoryName(dotNetPath) + ".acquisition_log.txt");
            return new AcquireDotNetTestStep(
                remoteCliZipPath,
                localCliZipPath,
                localCliTarPath,
                localCliExpandedDirPath,
                dotNetPath,
                acquireLogPath);
        }


        protected static string GetInitialSourceDirPath(TestConfiguration config, string debuggeeName)
        {
            return Path.Combine(config.DebuggeeSourceRoot, debuggeeName);
        }

        protected static string GetDebuggeeNativeLibDirPath(TestConfiguration config, string debuggeeName)
        {
            if (config.DebuggeeNativeLibRoot == null)
            {
                return null;
            }
            return Path.Combine(config.DebuggeeNativeLibRoot, debuggeeName);
        }

        protected static string GetDebuggeeSolutionDirPath(string dotNetRootBuildDirPath, string debuggeeName)
        {
            return Path.Combine(dotNetRootBuildDirPath, debuggeeName);
        }

        protected static string GetDotNetRootBuildDirPath(TestConfiguration config)
        {
            return config.DebuggeeBuildRoot;
        }

        protected static string GetDebuggeeProjectDirPath(string debuggeeSolutionDirPath, string initialSourceDirPath, string debuggeeName)
        {
            string debuggeeProjectDirPath = debuggeeSolutionDirPath;
            if (Directory.Exists(Path.Combine(initialSourceDirPath, debuggeeName)))
            {
                debuggeeProjectDirPath = Path.Combine(debuggeeSolutionDirPath, debuggeeName);
            }
            return debuggeeProjectDirPath;
        }

        protected virtual string GetDebuggeeBinaryDirPath(string debuggeeProjectDirPath, string framework, string runtime)
        {
            string debuggeeBinaryDirPath;
            if (runtime != null)
            {
                debuggeeBinaryDirPath = Path.Combine(debuggeeProjectDirPath, "bin", "Debug", framework, runtime);
            }
            else
            {
                debuggeeBinaryDirPath = Path.Combine(debuggeeProjectDirPath, "bin", "Debug", framework);
            }
            return debuggeeBinaryDirPath;
        }

        protected static string GetDebuggeeBinaryDllPath(TestConfiguration config, string debuggeeBinaryDirPath, string debuggeeName)
        {
            return config.IsNETCore ? Path.Combine(debuggeeBinaryDirPath, debuggeeName + ".dll") : null;
        }

        protected static string GetDebuggeeBinaryExePath(TestConfiguration config, string debuggeeBinaryDirPath, string debuggeeName)
        {
            return config.IsDesktop || config.PublishSingleFile ? Path.Combine(debuggeeBinaryDirPath, debuggeeName + (OS.Kind == OSKind.Windows ? ".exe" : "")) : null;
        }

        protected static string GetLogPath(TestConfiguration config, string framework, string runtime, string debuggeeName)
        {
            return Path.Combine(GetDotNetRootBuildDirPath(config), $"{framework}-{runtime ?? "any"}-{debuggeeName}.txt");
        }

        protected static Dictionary<string, string> GetNugetFeeds(TestConfiguration config)
        {
            Dictionary<string, string> nugetFeeds = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(config.NuGetPackageFeeds))
            {
                string[] feeds = config.NuGetPackageFeeds.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string feed in feeds)
                {
                    string[] feedParts = feed.Trim().Split('=');
                    if (feedParts.Length != 2)
                    {
                        throw new Exception("Expected feed \'" + feed + "\' to value <key>=<value> format");
                    }
                    nugetFeeds.Add(feedParts[0], feedParts[1]);
                }
            }
            return nugetFeeds;
        }

        protected static string GetRuntime(TestConfiguration config)
        {
            return config.BuildProjectRuntime;
        }

        protected abstract string GetFramework(TestConfiguration config);

        //we anticipate source paths like this:
        //InitialSource:        <DebuggeeSourceRoot>/<DebuggeeName>
        //DebuggeeNativeLibDir: <DebuggeeNativeLibRoot>/<DebuggeeName>
        //DotNetRootBuildDir:   <DebuggeeBuildRoot>
        //DebuggeeSolutionDir:  <DebuggeeBuildRoot>/<DebuggeeName>
        //DebuggeeProjectDir:   <DebuggeeBuildRoot>/<DebuggeeName>[/<DebuggeeName>]
        //DebuggeeBinaryDir:    <DebuggeeBuildRoot>/<DebuggeeName>[/<DebuggeeName>]/bin/Debug/<framework>/[<runtime>]
        //DebuggeeBinaryDll:    <DebuggeeBuildRoot>/<DebuggeeName>[/<DebuggeeName>]/bin/Debug/<framework>/<DebuggeeName>.dll
        //DebuggeeBinaryExe:    <DebuggeeBuildRoot>/<DebuggeeName>[/<DebuggeeName>]/bin/Debug/<framework>/[<runtime>]/<DebuggeeName>.exe
        //LogPath:              <DebuggeeBuildRoot>/<DebuggeeName>.txt

        // When the runtime directory is present it will have a native host exe in it that has been renamed to the debugee
        // name. It also has a managed dll in it which functions as a managed exe when renamed.
        // When the runtime directory is missing, the framework directory will have a managed dll in it that functions if it
        // is renamed to an exe. I'm sure that renaming isn't the intended usage, but it works and produces less churn
        // in our tests for the moment.
        public abstract DotNetBuildDebuggeeTestStep ConfigureDotNetBuildDebuggeeTask(TestConfiguration config, string dotNetPath, string cliToolsVersion, string debuggeeName);
    }
}

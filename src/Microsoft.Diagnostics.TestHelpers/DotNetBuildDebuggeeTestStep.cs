// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace Microsoft.Diagnostics.TestHelpers
{
    /// <summary>
    /// This test step builds debuggees using the dotnet tools
    /// </summary>
    /// <remarks>
    /// The build process consists of the following steps:
    ///   1. Create a source directory with the conventions that dotnet expects by copying it from the DebuggeeSourceRoot. Any template project
    ///      file that is found will be specialized by the implementation class.
    ///   2. Run dotnet restore in the newly created source directory
    ///   3. Run dotnet build in the same directory
    ///   4. Rename the built debuggee dll to an exe (so that it conforms to some historical conventions our tests expect)
    ///   5. Copy any native dependencies from the DebuggeeNativeLibRoot to the debuggee output directory
    /// </remarks>
    public abstract class DotNetBuildDebuggeeTestStep : TestStep
    {
        /// <summary>
        /// Create a new DotNetBuildDebuggeeTestStep.
        /// </summary>
        /// <param name="dotnetToolPath">
        /// The path to the dotnet executable
        /// </param>
        /// <param name="templateSolutionDirPath">
        /// The path to the template solution source. This will be copied into the final solution source directory
        /// under debuggeeSolutionDirPath and any project.template.json files it contains will be specialized.
        /// </param>
        /// <param name="debuggeeNativeLibDirPath">
        /// The path where the debuggee's native binary dependencies will be copied from.
        /// </param>
        /// <param name="debuggeeSolutionDirPath">
        /// The path where the debuggee solution will be created. For single project solutions this will be identical to
        /// the debuggee project directory.
        /// </param>
        /// <param name="debuggeeProjectDirPath">
        /// The path where the primary debuggee executable project directory will be created. For single project solutions this
        /// will be identical to the debuggee solution directory.
        /// </param>
        /// <param name="debuggeeBinaryDirPath">
        /// The directory path where the dotnet tool will place the compiled debuggee binaries.
        /// </param>
        /// <param name="debuggeeBinaryDllPath">
        /// The path where the dotnet tool will place the compiled debuggee assembly.
        /// </param>
        /// <param name="debuggeeBinaryExePath">
        /// The path to which the build will copy the debuggee binary dll with a .exe extension.
        /// </param>
        /// <param name="nugetPackageCacheDirPath">
        /// The path to the NuGet package cache. If null, no value for this setting will be placed in the
        /// NuGet.config file and dotnet will need to read it from other ambient NuGet.config files or infer
        /// a default cache.
        /// </param>
        /// <param name="nugetFeeds">
        /// A mapping of nuget feed names to locations. These feeds will be used to restore debuggee
        /// nuget package dependencies.
        /// </param>
        /// <param name="logPath">
        /// The path where the build output will be logged
        /// </param>
        public DotNetBuildDebuggeeTestStep(string dotnetToolPath,
                                       string templateSolutionDirPath,
                                       string debuggeeMsbuildAuxRoot,
                                       string debuggeeNativeLibDirPath,
                                       string debuggeeSolutionDirPath,
                                       string debuggeeProjectDirPath,
                                       string debuggeeBinaryDirPath,
                                       string debuggeeBinaryDllPath,
                                       string debuggeeBinaryExePath,
                                       string nugetPackageCacheDirPath,
                                       Dictionary<string,string> nugetFeeds,
                                       string logPath) :
            base(logPath, "Build Debuggee")
        {
            DotNetToolPath = dotnetToolPath;
            DebuggeeTemplateSolutionDirPath = templateSolutionDirPath;
            DebuggeeMsbuildAuxRoot = debuggeeMsbuildAuxRoot;
            DebuggeeNativeLibDirPath = debuggeeNativeLibDirPath;
            DebuggeeSolutionDirPath = debuggeeSolutionDirPath;
            DebuggeeProjectDirPath = debuggeeProjectDirPath;
            DebuggeeBinaryDirPath = debuggeeBinaryDirPath;
            DebuggeeBinaryDllPath = debuggeeBinaryDllPath;
            DebuggeeBinaryExePath = debuggeeBinaryExePath;
            NuGetPackageCacheDirPath = nugetPackageCacheDirPath;
            NugetFeeds = nugetFeeds;
            if(NugetFeeds != null && NugetFeeds.Count > 0)
            {
                NuGetConfigPath = Path.Combine(DebuggeeSolutionDirPath, "NuGet.config");
            }
        }

        /// <summary>
        /// The path to the dotnet executable
        /// </summary>
        public string DotNetToolPath { get; private set; }
        /// <summary>
        /// The path to the template solution source. This will be copied into the final solution source directory
        /// under debuggeeSolutionDirPath and any project.template.json files it contains will be specialized.
        /// </summary>
        public string DebuggeeTemplateSolutionDirPath { get; private set; }
        /// <summary>
        /// The path containing supporting msbuild files for the build
        /// </summary>
        public string DebuggeeMsbuildAuxRoot { get; }
        /// <summary>
        /// The path where the debuggee's native binary dependencies will be copied from.
        /// </summary>
        public string DebuggeeNativeLibDirPath { get; private set; }
        /// <summary>
        /// The path where the debuggee solution will be created. For single project solutions this will be identical to
        /// the debuggee project directory.
        /// </summary>
        public string DebuggeeSolutionDirPath { get; private set; }

        /// <summary>
        /// The path where the primary debuggee executable project directory will be created. For single project solutions this
        /// will be identical to the debuggee solution directory.
        /// </summary>
        public string DebuggeeProjectDirPath { get; private set; }
        /// <summary>
        /// The directory path where the dotnet tool will place the compiled debuggee binaries.
        /// </summary>
        public string DebuggeeBinaryDirPath { get; private set; }
        /// <summary>
        /// The path where the dotnet tool will place the compiled debuggee assembly.
        /// </summary>
        public string DebuggeeBinaryDllPath { get; private set; }
        /// <summary>
        /// The path to which the build will copy the debuggee binary dll with a .exe extension.
        /// </summary>
        public string DebuggeeBinaryExePath { get; private set; }
        /// The path to the NuGet package cache. If null, no value for this setting will be placed in the
        /// NuGet.config file and dotnet will need to read it from other ambient NuGet.config files or infer
        /// a default cache.
        public string NuGetPackageCacheDirPath { get; private set; }
        public string NuGetConfigPath { get; private set; }
        public IDictionary<string,string> NugetFeeds { get; private set; }
        public abstract string ProjectTemplateFileName { get; }

        async protected override Task DoWork(ITestOutputHelper output)
        {
            PrepareProjectSolution(output);
            await Restore(output);
            await Build(output);
            CopyNativeDependencies(output);
        }

        private void PrepareProjectSolution(ITestOutputHelper output)
        {
            AssertDebuggeeSolutionTemplateDirExists(output);

            output.WriteLine("Creating Solution Source Directory");
            output.WriteLine("{");
            IndentedTestOutputHelper indentedOutput = new IndentedTestOutputHelper(output);
            CopySourceDirectory(DebuggeeTemplateSolutionDirPath, DebuggeeSolutionDirPath, indentedOutput);
            CopySourceDirectory(DebuggeeMsbuildAuxRoot, DebuggeeSolutionDirPath, indentedOutput);
            CreateNuGetConfig(indentedOutput);
            output.WriteLine("}");
            output.WriteLine("");

            AssertDebuggeeSolutionDirExists(output);
            AssertDebuggeeProjectDirExists(output);
            AssertDebuggeeProjectFileExists(output);
        }

        private SemaphoreSlim _dotnetRestoreLock = new SemaphoreSlim(1);

        protected async Task Restore(string extraArgs, ITestOutputHelper output)
        {
            AssertDebuggeeSolutionDirExists(output);
            AssertDebuggeeProjectDirExists(output);
            AssertDebuggeeProjectFileExists(output);

            string args = "restore";
            if (NuGetConfigPath != null)
            {
                args += " --configfile " + NuGetConfigPath;
            }
            if (NuGetPackageCacheDirPath != null)
            {
                args += " --packages \"" + NuGetPackageCacheDirPath.TrimEnd('\\') + "\"";
            }
            if (extraArgs != null)
            {
                args += extraArgs;
            }
            output.WriteLine("Launching {0} {1}", DotNetToolPath, args);
            ProcessRunner runner = new ProcessRunner(DotNetToolPath, args).
                WithEnvironmentVariable("DOTNET_MULTILEVEL_LOOKUP", "0").
                WithEnvironmentVariable("DOTNET_ROOT", Path.GetDirectoryName(DotNetToolPath)).
                WithEnvironmentVariable("DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER", "true").
                WithEnvironmentVariable("DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR", Path.GetDirectoryName(DotNetToolPath)).
                WithEnvironmentVariable("DOTNET_INSTALL_DIR", Path.GetDirectoryName(DotNetToolPath)).
                RemoveEnvironmentVariable("MSBuildSDKsPath").
                WithWorkingDirectory(DebuggeeSolutionDirPath).
                WithLog(output).
                WithTimeout(TimeSpan.FromMinutes(10)).                    // restore can be painfully slow
                WithExpectedExitCode(0);

            if (OS.Kind != OSKind.Windows && Environment.GetEnvironmentVariable("HOME") == null)
            {
                output.WriteLine("Detected HOME environment variable doesn't exist. This will trigger a bug in dotnet restore.");
                output.WriteLine("See: https://github.com/NuGet/Home/issues/2960");
                output.WriteLine("Test will workaround this by manually setting a HOME value");
                output.WriteLine("");
                runner = runner.WithEnvironmentVariable("HOME", DebuggeeSolutionDirPath);
            }

            //workaround for https://github.com/dotnet/cli/issues/3868
            await _dotnetRestoreLock.WaitAsync();
            try
            {
                await runner.Run();
            }
            finally
            {
                _dotnetRestoreLock.Release();
            }

            AssertDebuggeeAssetsFileExists(output);
        }

        protected virtual async Task Restore(ITestOutputHelper output)
        {
            await Restore(null, output);
        }

        protected async Task Build(string dotnetArgs, ITestOutputHelper output)
        {
            AssertDebuggeeSolutionDirExists(output);
            AssertDebuggeeProjectFileExists(output);
            AssertDebuggeeAssetsFileExists(output);

            output.WriteLine("Launching {0} {1}", DotNetToolPath, dotnetArgs);
            ProcessRunner runner = new ProcessRunner(DotNetToolPath, dotnetArgs).
                WithEnvironmentVariable("DOTNET_MULTILEVEL_LOOKUP", "0").
                WithEnvironmentVariable("DOTNET_ROOT", Path.GetDirectoryName(DotNetToolPath)).
                WithEnvironmentVariable("DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER", "true").
                WithEnvironmentVariable("DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR", Path.GetDirectoryName(DotNetToolPath)).
                WithEnvironmentVariable("DOTNET_INSTALL_DIR", Path.GetDirectoryName(DotNetToolPath)).
                RemoveEnvironmentVariable("MSBuildSDKsPath").
                WithWorkingDirectory(DebuggeeProjectDirPath).
                WithLog(output).
                WithTimeout(TimeSpan.FromMinutes(10)). // a mac CI build of the modules debuggee is painfully slow :(
                WithExpectedExitCode(0);

            if (OS.Kind != OSKind.Windows && Environment.GetEnvironmentVariable("HOME") == null)
            {
                output.WriteLine("Detected HOME environment variable doesn't exist. This will trigger a bug in dotnet build.");
                output.WriteLine("See: https://github.com/NuGet/Home/issues/2960");
                output.WriteLine("Test will workaround this by manually setting a HOME value");
                output.WriteLine("");
                runner = runner.WithEnvironmentVariable("HOME", DebuggeeSolutionDirPath);
            }
            if (NuGetPackageCacheDirPath != null)
            {
                //dotnet restore helpfully documents its --packages argument in the help text, but
                //NUGET_PACKAGES was undocumented as far as I noticed. If this stops working we can also
                //auto-generate a global.json with a "packages" setting, but this was more expedient.
                runner = runner.WithEnvironmentVariable("NUGET_PACKAGES", NuGetPackageCacheDirPath);
            }

            await runner.Run();

            if (DebuggeeBinaryExePath != null)
            {
                AssertDebuggeeExeExists(output);
            }
            else
            {
                AssertDebuggeeDllExists(output);
            }
        }

        protected virtual async Task Build(ITestOutputHelper output)
        {
            await Build("build", output);
        }

        private void CopyNativeDependencies(ITestOutputHelper output)
        {
            if (Directory.Exists(DebuggeeNativeLibDirPath))
            {
                foreach (string filePath in Directory.EnumerateFiles(DebuggeeNativeLibDirPath))
                {
                    string targetPath = Path.Combine(DebuggeeBinaryDirPath, Path.GetFileName(filePath));
                    output.WriteLine("Copying: " + filePath + " -> " + targetPath);
                    File.Copy(filePath, targetPath);
                }
            }
        }

        private void CopySourceDirectory(string sourceDirPath, string destDirPath, ITestOutputHelper output)
        {
            output.WriteLine("Copying: " + sourceDirPath + " -> " + destDirPath);
            Directory.CreateDirectory(destDirPath);
            foreach(string dirPath in Directory.EnumerateDirectories(sourceDirPath))
            {
                CopySourceDirectory(dirPath, Path.Combine(destDirPath, Path.GetFileName(dirPath)), output);
            }
            foreach (string filePath in Directory.EnumerateFiles(sourceDirPath))
            {
                string fileName = Path.GetFileName(filePath);
                if (fileName == ProjectTemplateFileName)
                {
                    ExpandProjectTemplate(filePath, destDirPath, output);
                }
                else
                {
                    File.Copy(filePath, Path.Combine(destDirPath, Path.GetFileName(filePath)), true);
                }
            }
        }

        protected abstract void ExpandProjectTemplate(string filePath, string destDirPath, ITestOutputHelper output);

        protected void CreateNuGetConfig(ITestOutputHelper output)
        {
            if (NuGetConfigPath == null)
            {
                return;
            }
            string nugetConfigPath = Path.Combine(DebuggeeSolutionDirPath, "NuGet.config");
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<configuration>");
            if(NugetFeeds != null && NugetFeeds.Count > 0)
            {
                sb.AppendLine("  <packageSources>");
                sb.AppendLine("    <clear />");
                foreach(KeyValuePair<string, string> kv in NugetFeeds)
                {
                    sb.AppendLine("    <add key=\"" + kv.Key + "\" value=\"" + kv.Value + "\" />");
                }
                sb.AppendLine("  </packageSources>");
                sb.AppendLine("  <activePackageSource>");
                sb.AppendLine("    <add key=\"All\" value=\"(Aggregate source)\" />");
                sb.AppendLine("  </activePackageSource>");
            }
            sb.AppendLine("</configuration>");

            output.WriteLine("Creating: " + NuGetConfigPath);
            File.WriteAllText(NuGetConfigPath, sb.ToString());
        }

        protected void AssertDebuggeeSolutionTemplateDirExists(ITestOutputHelper output)
        {
            AssertX.DirectoryExists("debuggee solution template directory", DebuggeeTemplateSolutionDirPath, output);
        }

        protected void AssertDebuggeeProjectDirExists(ITestOutputHelper output)
        {
            AssertX.DirectoryExists("debuggee project directory", DebuggeeProjectDirPath, output);
        }

        protected void AssertDebuggeeSolutionDirExists(ITestOutputHelper output)
        {
            AssertX.DirectoryExists("debuggee solution directory", DebuggeeSolutionDirPath, output);
        }

        protected void AssertDebuggeeDllExists(ITestOutputHelper output)
        {
            AssertX.FileExists("debuggee dll", DebuggeeBinaryDllPath, output);
        }

        protected void AssertDebuggeeExeExists(ITestOutputHelper output)
        {
            AssertX.FileExists("debuggee exe", DebuggeeBinaryExePath, output);
        }

        protected abstract void AssertDebuggeeAssetsFileExists(ITestOutputHelper output);

        protected abstract void AssertDebuggeeProjectFileExists(ITestOutputHelper output);
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xunit.Abstractions;

namespace Microsoft.Diagnostics.TestHelpers
{
    /// <summary>
    /// This test step builds debuggees using the dotnet tools with .csproj projects files.
    /// </summary>
    /// <remarks>
    /// Any <debuggee name>.csproj file that is found will be specialized by adding a linker package reference.
    /// This lets us decide the runtime and dependency versions at test execution time.
    /// </remarks>
    public class CsprojBuildDebuggeeTestStep : DotNetBuildDebuggeeTestStep
    {
        /// <param name="buildProperties">
        /// A mapping from .csproj property strings to their values. These properties will be set when building the debuggee.
        /// </param>
        /// <param name="runtimeIdentifier">
        /// The runtime moniker to be built.
        /// </param>
        public CsprojBuildDebuggeeTestStep(string dotnetToolPath,
                                       string templateSolutionDirPath,
                                       string debuggeeMsbuildAuxRoot,
                                       string debuggeeNativeLibDirPath,
                                       Dictionary<string, string> buildProperties,
                                       string runtimeIdentifier,
                                       string runtimeFramework,
                                       string linkerPackageVersion,
                                       string debuggeeName,
                                       string debuggeeSolutionDirPath,
                                       string debuggeeProjectDirPath,
                                       string debuggeeBinaryDirPath,
                                       string debuggeeBinaryDllPath,
                                       string debuggeeBinaryExePath,
                                       string nugetPackageCacheDirPath,
                                       Dictionary<string, string> nugetFeeds,
                                       string logPath) :
            base(dotnetToolPath,
                 templateSolutionDirPath,
                 debuggeeMsbuildAuxRoot,
                 debuggeeNativeLibDirPath,
                 debuggeeSolutionDirPath,
                 debuggeeProjectDirPath,
                 debuggeeBinaryDirPath,
                 debuggeeBinaryDllPath,
                 debuggeeBinaryExePath,
                 nugetPackageCacheDirPath,
                 nugetFeeds,
                 logPath)
        {
            BuildProperties = buildProperties;
            RuntimeIdentifier = runtimeIdentifier;
            RuntimeFramework = runtimeFramework;
            DebuggeeName = debuggeeName;
            ProjectTemplateFileName = debuggeeName + ".csproj";
            LinkerPackageVersion = linkerPackageVersion;
        }

        /// <summary>
        /// A mapping from .csproj property strings to their values. These properties will be set when building the debuggee.
        /// </summary>
        public IDictionary<string, string> BuildProperties { get; }
        public string RuntimeIdentifier { get; }
        public string RuntimeFramework { get; }
        public string DebuggeeName { get; }
        public string LinkerPackageVersion { get; }
        public override string ProjectTemplateFileName { get; }

        protected override async Task Restore(ITestOutputHelper output)
        {
            string extraArgs = "";
            if (RuntimeIdentifier != null)
            {
                extraArgs += " --runtime " + RuntimeIdentifier;
            }
            foreach (KeyValuePair<string, string> prop in BuildProperties)
            {
                extraArgs += $" /p:{prop.Key}={prop.Value}";
            }
            await Restore(extraArgs, output);
        }

        protected override async Task Build(ITestOutputHelper output)
        {
            string publishArgs = "publish --configuration Debug";
            if (RuntimeFramework != null)
            {
                publishArgs += " --framework " + RuntimeFramework;
            }
            if (RuntimeIdentifier != null)
            {
                publishArgs += " --runtime " + RuntimeIdentifier;
                publishArgs += " --self-contained true";
            }
            foreach (KeyValuePair<string, string> prop in BuildProperties)
            {
                publishArgs += $" /p:{prop.Key}={prop.Value}";
            }
            await Build(publishArgs, output);
        }

        protected override void ExpandProjectTemplate(string filePath, string destDirPath, ITestOutputHelper output)
        {
            ConvertCsprojTemplate(filePath, Path.Combine(destDirPath, DebuggeeName + ".csproj"));
        }

        private void ConvertCsprojTemplate(string csprojTemplatePath, string csprojOutPath)
        {
            var xdoc = XDocument.Load(csprojTemplatePath);
            XNamespace ns = xdoc.Root.GetDefaultNamespace();
            if (LinkerPackageVersion != null)
            {
                AddLinkerPackageReference(xdoc, ns, LinkerPackageVersion);
            }
            using (var fs = new FileStream(csprojOutPath, FileMode.Create))
            {
                xdoc.Save(fs);
            }
        }

        private static void AddLinkerPackageReference(XDocument xdoc, XNamespace ns, string linkerPackageVersion)
        {
            xdoc.Root.Add(new XElement(ns + "ItemGroup",
                                       new XElement(ns + "PackageReference",
                                                    new XAttribute("Include", "ILLink.Tasks"),
                                                    new XAttribute("Version", linkerPackageVersion))));
        }

        protected override void AssertDebuggeeAssetsFileExists(ITestOutputHelper output)
        {
            AssertX.FileExists("debuggee project.assets.json", Path.Combine(DebuggeeProjectDirPath, "obj", "project.assets.json"), output);
        }

        protected override void AssertDebuggeeProjectFileExists(ITestOutputHelper output)
        {
            AssertX.FileExists("debuggee csproj", Path.Combine(DebuggeeProjectDirPath, DebuggeeName + ".csproj"), output);
        }
    }
}

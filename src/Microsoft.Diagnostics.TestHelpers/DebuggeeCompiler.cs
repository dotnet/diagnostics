// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Diagnostics.TestHelpers
{
    /// <summary>
    /// DebugeeCompiler is responsible for finding and/or producing the source and binaries of a given debuggee.
    /// The steps it takes to do this depend on the TestConfiguration.
    /// </summary>
    public static class DebuggeeCompiler
    {
        public static async Task<DebuggeeConfiguration> Execute(TestConfiguration config, string debuggeeName, ITestOutputHelper output)
        {
            IDebuggeeCompiler compiler;
            switch (config.DebuggeeBuildProcess)
            {
                case "prebuilt":
                    // Backwards compatibility for the diagnostics test repo
                    compiler = new PrebuiltDebuggeeCompiler(config, debuggeeName);
                    break;
                case "sdk.prebuilt":
                    // The .NET Core SDK layout
                    compiler = new SdkPrebuiltDebuggeeCompiler(config, debuggeeName);
                    break;
                case "cli":
                    // Builds the debuggee with the .NET Core CLI
                    compiler = new CliDebuggeeCompiler(config, debuggeeName);
                    break;
                default:
                    throw new Exception("Invalid DebuggeeBuildProcess configuration value. Expected 'prebuilt', actual \'" + config.DebuggeeBuildProcess + "\'");
            }
            DebuggeeConfiguration debuggee = await compiler.Execute(output).ConfigureAwait(false);
            if (config.PublishSingleFile)
            {
                string suffix = OS.Kind == OSKind.Windows ? ".dll" : OS.Kind == OSKind.OSX ? ".dylib" : ".so";
                string prefix = OS.Kind == OSKind.Windows ? "" : "lib";
                string dacPath = TestConfiguration.MakeCanonicalPath(config.GetValue("DacModulePath"));
                string dbiPath = TestConfiguration.MakeCanonicalPath(config.GetValue("DbiModulePath"));
                if (!string.IsNullOrEmpty(config.RuntimeSymbolsPath))
                {
                    dacPath ??= Path.Combine(config.RuntimeSymbolsPath, prefix + "mscordaccore" + suffix);
                    dbiPath ??= Path.Combine(config.RuntimeSymbolsPath, prefix + "mscordbi" + suffix);
                }
                SingleFileRuntimeValidator.Validate(debuggee.BinaryExePath, dacPath, dbiPath);
            }
            return debuggee;
        }
    }

    public interface IDebuggeeCompiler
    {
        Task<DebuggeeConfiguration> Execute(ITestOutputHelper output);
    }

    public class DebuggeeConfiguration
    {
        public DebuggeeConfiguration(string sourcePath, string binaryDirPath, string binaryExePath)
        {
            SourcePath = sourcePath;
            BinaryDirPath = binaryDirPath;
            BinaryExePath = binaryExePath;
        }
        public string SourcePath { get; private set; }
        public string BinaryDirPath { get; private set; }
        public string BinaryExePath { get; private set; }
    }
}

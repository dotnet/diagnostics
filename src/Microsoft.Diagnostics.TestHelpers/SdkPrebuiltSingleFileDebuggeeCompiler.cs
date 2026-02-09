// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace Microsoft.Diagnostics.TestHelpers
{
    /// <summary>
    /// Compiler for prebuilt single-file debuggees.
    /// Single-file debuggees are published at build time and stored at:
    /// &lt;DebuggeeBuildRoot&gt;/bin/&lt;DebuggeeName&gt;/SingleFile/&lt;TargetConfiguration&gt;/&lt;BuildProjectFramework&gt;/&lt;BuildProjectRuntime&gt;/
    /// </summary>
    public class SdkPrebuiltSingleFileDebuggeeCompiler : IDebuggeeCompiler
    {
        private readonly string _sourcePath;
        private readonly string _binaryPath;
        private readonly string _binaryExePath;

        public SdkPrebuiltSingleFileDebuggeeCompiler(TestConfiguration config, string debuggeeName)
        {
            if (string.IsNullOrEmpty(config.TargetConfiguration))
            {
                throw new System.ArgumentException("TargetConfiguration must be set in the TestConfiguration");
            }
            if (string.IsNullOrEmpty(config.BuildProjectFramework))
            {
                throw new System.ArgumentException("BuildProjectFramework must be set in the TestConfiguration");
            }
            if (string.IsNullOrEmpty(config.BuildProjectRuntime))
            {
                throw new System.ArgumentException("BuildProjectRuntime must be set in the TestConfiguration for single-file debuggees");
            }

            // The layout for single-file debuggees:
            // Source Path:     <DebuggeeSourceRoot>/<DebuggeeName>/[<DebuggeeName>]
            // Binary Path:     <DebuggeeBuildRoot>/bin/<DebuggeeName>/SingleFile/<TargetConfiguration>/<BuildProjectFramework>/<BuildProjectRuntime>
            // Binary Exe Path: <BinaryPath>/<DebuggeeName>.exe (Windows) or <DebuggeeName> (Unix)
            _sourcePath = Path.Combine(config.DebuggeeSourceRoot, debuggeeName);
            if (Directory.Exists(Path.Combine(_sourcePath, debuggeeName)))
            {
                _sourcePath = Path.Combine(_sourcePath, debuggeeName);
            }
            _binaryPath = Path.Combine(
                config.DebuggeeBuildRoot,
                "bin",
                debuggeeName,
                "SingleFile",
                config.TargetConfiguration,
                config.BuildProjectFramework,
                config.BuildProjectRuntime);

            // Single-file produces a native executable
            string exeExtension = OS.Kind == OSKind.Windows ? ".exe" : "";
            _binaryExePath = Path.Combine(_binaryPath, debuggeeName + exeExtension);
        }

        public Task<DebuggeeConfiguration> Execute(ITestOutputHelper output)
        {
            return Task.FromResult(new DebuggeeConfiguration(_sourcePath, _binaryPath, _binaryExePath));
        }
    }
}

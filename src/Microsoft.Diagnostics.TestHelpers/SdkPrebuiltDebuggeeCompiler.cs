// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Diagnostics.TestHelpers
{
    public class SdkPrebuiltDebuggeeCompiler : IDebuggeeCompiler
    {
        private readonly string _sourcePath;
        private readonly string _binaryPath;
        private readonly string _binaryExePath;

        public SdkPrebuiltDebuggeeCompiler(TestConfiguration config, string debuggeeName)
        {
            if (string.IsNullOrEmpty(config.TargetConfiguration))
            {
                throw new System.ArgumentException("TargetConfiguration must be set in the TestConfiguration");
            }
            if (string.IsNullOrEmpty(config.BuildProjectFramework))
            {
                throw new System.ArgumentException("BuildProjectFramework must be set in the TestConfiguration");
            }

            // The layout is how the current .NET Core SDK layouts the binaries out:
            // Source Path:     <DebuggeeSourceRoot>/<DebuggeeName>/[<DebuggeeName>]
            // Binary Path:     <DebuggeeBuildRoot>/bin/<DebuggeeName>/<TargetConfiguration>/<BuildProjectFramework>
            // Binary Exe Path: <DebuggeeBuildRoot>/bin/<DebuggeeName>/<TargetConfiguration>/<BuildProjectFramework>/<DebuggeeName>.dll
            // Single-file apps use the RID-specific publish directory and launch the apphost directly.
            _sourcePath = Path.Combine(config.DebuggeeSourceRoot, debuggeeName);
            if (Directory.Exists(Path.Combine(_sourcePath, debuggeeName)))
            {
                _sourcePath = Path.Combine(_sourcePath, debuggeeName);
            }
            _binaryPath = Path.Combine(config.DebuggeeBuildRoot, "bin", debuggeeName, config.TargetConfiguration, config.BuildProjectFramework);
            if (config.PublishSingleFile)
            {
                if (string.IsNullOrEmpty(config.BuildProjectRuntime))
                {
                    throw new System.ArgumentException("BuildProjectRuntime must be set for a prebuilt single-file debuggee");
                }
                _binaryPath = Path.Combine(_binaryPath, config.BuildProjectRuntime, "publish");
            }
            string extension = config.PublishSingleFile ? (OS.Kind == OSKind.Windows ? ".exe" : "") : (config.IsDesktop ? ".exe" : ".dll");
            _binaryExePath = Path.Combine(_binaryPath, debuggeeName) + extension;
        }

        public Task<DebuggeeConfiguration> Execute(ITestOutputHelper output)
        {
            return Task.FromResult(new DebuggeeConfiguration(_sourcePath, _binaryPath, _binaryExePath));
        }
    }
}

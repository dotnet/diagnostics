﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace Microsoft.Diagnostics.TestHelpers
{
    public class SdkPrebuiltDebuggeeCompiler : IDebuggeeCompiler
    {
        readonly string _sourcePath;
        readonly string _binaryPath;
        readonly string _binaryExePath;

        public SdkPrebuiltDebuggeeCompiler(TestConfiguration config, string debuggeeName)
        {
            // The layout is how the current .NET Core SDK layouts the binaries out:
            // Source Path:     <DebuggeeSourceRoot>/<DebuggeeName>/[<DebuggeeName>]
            // Binary Path:     <DebuggeeBuildRoot>/bin/<DebuggeeName>/<TargetConfiguration>/<BuildProjectFramework>
            // Binary Exe Path: <DebuggeeBuildRoot>/bin/<DebuggeeName>/<TargetConfiguration>/<BuildProjectFramework>/<DebuggeeName>.dll
            _sourcePath = Path.Combine(config.DebuggeeSourceRoot, debuggeeName);
            if (Directory.Exists(Path.Combine(_sourcePath, debuggeeName)))
            {
                _sourcePath = Path.Combine(_sourcePath, debuggeeName);
            }
            _binaryPath = Path.Combine(config.DebuggeeBuildRoot, "bin", debuggeeName, config.TargetConfiguration, config.BuildProjectFramework);
            _binaryExePath = Path.Combine(_binaryPath, debuggeeName) + (config.IsDesktop ? ".exe" : ".dll");
        }

        public Task<DebuggeeConfiguration> Execute(ITestOutputHelper output)
        {
            return Task.Factory.StartNew<DebuggeeConfiguration>(() => new DebuggeeConfiguration(_sourcePath, _binaryPath, _binaryExePath));
        }
    }
}
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace Microsoft.Diagnostics.TestHelpers
{
    public class PrebuiltDebuggeeCompiler : IDebuggeeCompiler
    {
        private string _sourcePath;
        private string _binaryPath;
        private string _binaryExePath;

        public PrebuiltDebuggeeCompiler(TestConfiguration config, string debuggeeName)
        {
            //we anticipate paths like this:
            //Source:   <DebuggeeSourceRoot>/<DebuggeeName>/[<DebuggeeName>]
            //Binaries: <DebuggeeBuildRoot>/<DebuggeeName>/
            _sourcePath = Path.Combine(config.DebuggeeSourceRoot, debuggeeName);
            if (Directory.Exists(Path.Combine(_sourcePath, debuggeeName)))
            {
                _sourcePath = Path.Combine(_sourcePath, debuggeeName);
            }

            _binaryPath = Path.Combine(config.DebuggeeBuildRoot, debuggeeName);
            _binaryExePath = Path.Combine(_binaryPath, debuggeeName);
            _binaryExePath += ".exe";
        }

        public Task<DebuggeeConfiguration> Execute(ITestOutputHelper output)
        {
            return Task.FromResult<DebuggeeConfiguration>(new DebuggeeConfiguration(_sourcePath, _binaryPath, _binaryExePath));
        }
    }
}

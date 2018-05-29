using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Debugger.Tests.Build
{
    public class PrebuiltDebuggeeCompiler : IDebuggeeCompiler
    {
        string _sourcePath;
        string _binaryPath;
        string _binaryExePath;

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
            return Task.Factory.StartNew<DebuggeeConfiguration>(() => new DebuggeeConfiguration(_sourcePath, _binaryPath, _binaryExePath));
        }
    }
}
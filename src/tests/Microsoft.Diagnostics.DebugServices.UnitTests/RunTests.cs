// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Diagnostics.TestHelpers;
using Xunit.Abstractions;

namespace Microsoft.Diagnostics.DebugServices.UnitTests
{
    [Command(Name = "runtests", Help = "Runs the debug services xunit tests.")]
    public class RunTestsCommand : CommandBase, ITestOutputHelper
    {
        [ServiceImport]
        public ITarget Target { get; set; }

        [Argument(Help = "Test data xml file path.")]
        public string TestDataPath { get; set; }

        public override void Invoke()
        {
            IEnumerable<TestHost> configurations;
            if (TestDataPath != null)
            {
                Dictionary<string, string> initialConfig = new()
                {
                    ["OS"] = OS.Kind.ToString(),
                    ["TargetArchitecture"] = OS.TargetArchitecture.ToString().ToLowerInvariant(),
                    ["TestDataFile"] = TestDataPath,
                };
                configurations = new[] { new TestDebugger(new TestConfiguration(initialConfig), Target) };
            }
            else
            {
                TestConfiguration.BaseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                configurations = TestRunConfiguration.Instance.Configurations.Select((config) => new TestDebugger(config, Target));
            }
            using DebugServicesTests debugServicesTests = new(this);
            foreach (TestHost host in configurations)
            {
                if (!host.Config.IsTestDbgEng())
                {
                    try
                    {
                        debugServicesTests.TargetTests(host);
                        debugServicesTests.ModuleTests(host);
                        debugServicesTests.ThreadTests(host);
                        debugServicesTests.RuntimeTests(host);
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceError("Tests FAILED:");
                        Trace.TraceError(ex.ToString());
                        return;
                    }
                }
            }
            WriteLine("Tests PASSED");
        }

        #region ITestOutputHelper

        void ITestOutputHelper.WriteLine(string message) => WriteLine(message);

        void ITestOutputHelper.WriteLine(string format, params object[] args) => WriteLine(format, args);

        #endregion
    }

    internal class TestDebugger : TestHost
    {
        private readonly ITarget _target;

        internal TestDebugger(TestConfiguration config, ITarget target)
            : base(config)
        {
            _target = target;
        }

        protected override ITarget GetTarget() => _target;
    }
}

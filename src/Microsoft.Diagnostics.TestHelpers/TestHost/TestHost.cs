// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.Diagnostics.DebugServices;

namespace Microsoft.Diagnostics.TestHelpers
{
    public abstract class TestHost : IDisposable
    {
        private TestDataReader _testData;
        private ITarget _target;

        public readonly TestConfiguration Config;

        public TestHost(TestConfiguration config)
        {
            Config = config;
        }

        public virtual void Dispose()
        {
            _target?.Destroy();
            _target = null;
        }

        public TestDataReader TestData
        {
            get
            {
                _testData ??= TestDataFile != null ? new TestDataReader(TestDataFile) : null;
                return _testData;
            }
        }

        public ITarget Target
        {
            get
            {
                _target ??= GetTarget();
                return _target;
            }
        }

        public abstract IReadOnlyList<string> ExecuteHostCommand(string commandLine);

        protected abstract ITarget GetTarget();

        public string DumpFile => TestConfiguration.MakeCanonicalPath(Config.AllSettings["DumpFile"]);

        public string TestDataFile => TestConfiguration.MakeCanonicalPath(Config.AllSettings.GetValueOrDefault("TestDataFile"));

        public override string ToString() => DumpFile;
    }

    public static class TestHostExtensions
    {
        public static bool IsTestDbgEng(this TestConfiguration config) => config.AllSettings.GetValueOrDefault("TestDbgEng", string.Empty) == "true";
    }
}

using System;
using System.IO;
using Microsoft.Diagnostics.DebugServices;

namespace Microsoft.Diagnostics.TestHelpers
{
    public abstract class TestHost
    {
        private TestDataReader _testData;
        private ITarget _target;

        public readonly TestConfiguration Config;

        public TestHost(TestConfiguration config)
        {
            Config = config;
        }

        public TestDataReader TestData
        {
            get
            {
                _testData ??= new TestDataReader(TestDataFile);
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

        public bool IsTestDbgEng => Config.AllSettings.TryGetValue("TestDbgEng", out string value) && value == "true";

        protected abstract ITarget GetTarget();

        public string DumpFile => TestConfiguration.MakeCanonicalPath(Config.AllSettings["DumpFile"]);

        public string TestDataFile => TestConfiguration.MakeCanonicalPath(Config.AllSettings["TestDataFile"]);

        public override string ToString() => DumpFile;
    }

    public static class TestHostExtensions
    {
        public static bool IsTestDbgEng(this TestConfiguration config) => config.AllSettings.TryGetValue("TestDbgEng", out string value) && value == "true";
    }
}

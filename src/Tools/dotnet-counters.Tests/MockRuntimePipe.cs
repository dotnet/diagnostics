using System;
using System.IO;
using Xunit;

namespace dotnet_counters.Tests
{
    public class MockRuntimePipe
    {
        public MockRuntimePipe()
        {
            
        }

        [Fact]
        public void Test1()
        {
            var result = true;
            Assert.True(result, "Result should be true");
        }
    }
}

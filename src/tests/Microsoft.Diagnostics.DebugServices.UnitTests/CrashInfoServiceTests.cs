// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Xunit;

namespace Microsoft.Diagnostics.DebugServices.UnitTests
{
    public class CrashInfoServiceTests
    {
        [Fact]
        public void Create_AllowsMessageOnlyNativeAotCrashInfo()
        {
            string triageJson = """{"version":"1.0.0","runtime_base":"0x7FFF80270000","runtime_type":"4","runtime_version":"9.0.16","reason":"2","thread":"0x7050","message":"Delegate_GarbageCollected"}""";

            ICrashInfoService crashInfo = CrashInfoService.Create(0, Encoding.UTF8.GetBytes(triageJson), null!);

            Assert.NotNull(crashInfo);
            Assert.Equal(CrashReason.EnvironmentFailFast, crashInfo.CrashReason);
            Assert.Equal(RuntimeType.NativeAOT, crashInfo.RuntimeType);
            Assert.Equal((uint)0x7050, crashInfo.ThreadId);
            Assert.Equal("Delegate_GarbageCollected", crashInfo.Message);
            Assert.Null(crashInfo.GetException(0));
            Assert.Null(crashInfo.GetThreadException(crashInfo.ThreadId));
            Assert.Empty(crashInfo.GetNestedExceptions(crashInfo.ThreadId));
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Xunit;

namespace Microsoft.Diagnostics.DebugServices.UnitTests
{
    public class CrashInfoServiceTests
    {
        private const string TriageJson = """{"version":"1.0.0","runtime_base":"0x7FFF80270000","runtime_type":"4","runtime_version":"9.0.16","reason":"2","thread":"0x7050","message":"Delegate_GarbageCollected"}""";

        [Fact]
        public void CreateAllowsMessageOnlyNativeAotCrashInfo()
        {
            ICrashInfoService crashInfo = CrashInfoService.Create(0, Encoding.UTF8.GetBytes(TriageJson), null!);

            Assert.NotNull(crashInfo);
            Assert.Equal(CrashReason.EnvironmentFailFast, crashInfo.CrashReason);
            Assert.Equal(RuntimeType.NativeAOT, crashInfo.RuntimeType);
            Assert.Equal(0x7FFF80270000ul, crashInfo.RuntimeBaseAddress);
            Assert.Equal("9.0.16", crashInfo.RuntimeVersion);
            Assert.Equal((uint)0x7050, crashInfo.ThreadId);
            Assert.Equal("Delegate_GarbageCollected", crashInfo.Message);
            Assert.Null(crashInfo.GetException(0));
            Assert.Null(crashInfo.GetThreadException(crashInfo.ThreadId));
            Assert.Empty(crashInfo.GetNestedExceptions(crashInfo.ThreadId));
        }

        [Fact]
        public void CreateAllowsNumericValuesAndTrailingCommas()
        {
            string json = TriageJson.Replace("\"reason\":\"2\"", "\"reason\":2").Replace("\"runtime_type\":\"4\"", "\"runtime_type\":4");
            json = json[..^1] + ",}";
            ICrashInfoService crashInfo = CrashInfoService.Create(0, Encoding.UTF8.GetBytes(json), null!);

            Assert.NotNull(crashInfo);
            Assert.Equal(CrashReason.EnvironmentFailFast, crashInfo.CrashReason);
            Assert.Equal(RuntimeType.NativeAOT, crashInfo.RuntimeType);
            Assert.Equal(0x7FFF80270000ul, crashInfo.RuntimeBaseAddress);
            Assert.Equal(0x7050u, crashInfo.ThreadId);
        }

        [Fact]
        public void CreatePreservesNestedExceptionsAndStackFrames()
        {
            const string json = """
                {
                    "version": "1.0.0",
                    "thread": "0x7050",
                    "exception": {
                        "address": "0x1234567887654321",
                        "hr": "0x80131500",
                        "message": "outer",
                        "type": "System.Exception",
                        "stack": [{
                            "ip": "0x7FFF80270123",
                            "sp": "0x123456789000",
                            "module": "0x0",
                            "offset": "0x1234",
                            "name": "Test.Method"
                        }],
                        "inner": [{
                            "address": "0x1234567887654322",
                            "hr": "0x80131501",
                            "message": "inner",
                            "type": "System.SystemException"
                        }]
                    }
                }
                """;
            ICrashInfoService crashInfo = CrashInfoService.Create(0xDEADBEEFu, Encoding.UTF8.GetBytes(json), new TestModuleService());
            Assert.NotNull(crashInfo);
            Assert.Equal(0xDEADBEEFu, crashInfo.HResult);
            IException exception = crashInfo.GetException(0);
            Assert.NotNull(exception);
            Assert.Equal(0x1234567887654321ul, exception.Address);
            Assert.Equal(0x80131500u, exception.HResult);
            Assert.Equal("outer", exception.Message);
            Assert.Equal("System.Exception", exception.Type);
            Assert.Same(exception, crashInfo.GetThreadException(0x7050));
            Assert.Equal(1, exception.Stack.FrameCount);
            IStackFrame frame = exception.Stack.GetStackFrame(0);
            Assert.Equal(0x7FFF80270123ul, frame.InstructionPointer);
            Assert.Equal(0x123456789000ul, frame.StackPointer);
            Assert.Equal(0ul, frame.ModuleBase);
            frame.GetMethodName(out string moduleName, out string methodName, out ulong displacement);
            Assert.Null(moduleName);
            Assert.Equal("Test.Method", methodName);
            Assert.Equal(0x1234ul, displacement);
            IException inner = Assert.Single(exception.InnerExceptions);
            Assert.Equal(0x1234567887654322ul, inner.Address);
            Assert.Equal(0x80131501u, inner.HResult);
            Assert.Equal("inner", inner.Message);
            Assert.Equal("System.SystemException", inner.Type);
            Assert.Equal(0, inner.Stack.FrameCount);
            Assert.Empty(inner.InnerExceptions);
            Assert.Collection(crashInfo.GetNestedExceptions(0x7050),
                current => Assert.Same(exception, current),
                current => Assert.Equal(inner.Address, current.Address));
        }

        [Theory]
        [InlineData("")]
        [InlineData("{")]
        [InlineData("null")]
        [InlineData("{}")]
        [InlineData("""{"version":"invalid"}""")]
        [InlineData("""{"version":"0.9.0"}""")]
        [InlineData("""{"version":"1.0.0","thread":"invalid"}""")]
        [InlineData("""{"version":"1.0.0","thread":"0x100000000"}""")]
        [InlineData("""{"version":"1.0.0","runtime_base":"0x10000000000000000"}""")]
        public void CreateRejectsInvalidCrashInfo(string json)
        {
            Assert.Null(CrashInfoService.Create(0, Encoding.UTF8.GetBytes(json), null!));
        }

        private sealed class TestModuleService : IModuleService
        {
            public IModule EntryPointModule => throw new InvalidOperationException();
            public IEnumerable<IModule> EnumerateModules() => throw new InvalidOperationException();
            public IModule GetModuleFromIndex(int moduleIndex) => throw new InvalidOperationException();
            public IModule GetModuleFromBaseAddress(ulong baseAddress) => throw new InvalidOperationException();
            public IModule GetModuleFromAddress(ulong address) => throw new InvalidOperationException();
            public IEnumerable<IModule> GetModuleFromModuleName(string moduleName) => throw new InvalidOperationException();
            public IModule CreateModule(int moduleIndex, ulong imageBase, ulong imageSize, string imageName) => throw new InvalidOperationException();
        }
    }
}

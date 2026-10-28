// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

public sealed class VarargPInvokeTests
{
    public static TheoryData<TestConfig> Matrix =>
        TestConfig.BuildMatrix([TargetCatalog.VarargPInvoke], Flavor.Core | Flavor.SingleFile, Host.Cdb, Liveness.Live);

    [WindowsTheory]
    [MemberData(nameof(Matrix))]
    public async Task VarargPInvoke_ReportsInteropMethodAndDisassemblesStub(TestConfig config)
    {
        Assert.SkipWhen(
            RuntimeInformation.ProcessArchitecture == Architecture.X86,
            "https://github.com/dotnet/diagnostics/issues/5883 (dotnet/runtime#129456): vararg P/Invoke is not supported on Windows x86.");

        using LiveTarget target = (LiveTarget)await Targets.GetTargetAsync(config);
        target.RunToBreakpoint();

        target.Execute("bm msvcrt!printf");
        target.RunToBreakpoint();
        target.Execute("ln @$ip").AssertContains("msvcrt!printf");

        SosTable frames = target.Clrstack();
        const string InteropMethod = "VarargPInvokeInteropMD.Interop.printf(System.String, ...)";
        SosToken interopFrame = new("vararg P/Invoke frame", $@"\[InlinedCallFrame: {Sos.Addr.Pattern}\]\s+{Regex.Escape(InteropMethod)}");
        frames.AssertContainsRow(
            row => row["InternalFrame"].AsBoolean() && interopFrame.Matches(row["Call Site"]),
            $"an InlinedCallFrame for {InteropMethod}");

        const string StubName = "ILStubClass.IL_STUB_PInvoke(System.String, Int32, Double, ...)";
        const string StubNameWithSecretArgument = "ILStubClass.IL_STUB_PInvoke(System.String, Int32, Double, IntPtr, ...)";
        SosRow stub = frames.SingleRow(
            row => row["Function"] == StubName || row["Function"] == StubNameWithSecretArgument,
            $"Function is {StubName} or {StubNameWithSecretArgument}");
        string stubName = stub["Function"];
        ulong stubIp = stub["IP"].AsUInt64(Sos.Addr);
        MethodDumpResult method = target.Ip2md(stubIp);
        Assert.Equal(stubName, method.MethodName);
        Assert.True(method.IsJitted);
        Assert.NotEqual(0ul, method.MethodDesc!.Value);

        ClrUResult plain = target.ClrU(method.MethodDesc.Value);
        Assert.True(plain.HasNormalJitBanner);
        Assert.Equal(stubName, plain.MethodName);
        Assert.NotEqual(0ul, plain.Begin);
        Assert.True(plain.Size > 0);
        Assert.NotEmpty(plain.Instructions);
        Assert.DoesNotContain("Unknown error in reading function IL", plain.Output.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failed", plain.Output.Text, StringComparison.Ordinal);

        // IL stubs have no IL body: -il must still disassemble the same native code.
        ClrUResult withIl = target.ClrU(method.MethodDesc.Value, il: true);
        Assert.True(withIl.HasNormalJitBanner);
        Assert.Equal(plain.MethodName, withIl.MethodName);
        Assert.Equal(plain.Begin, withIl.Begin);
        Assert.Equal(plain.Size, withIl.Size);
        Assert.NotEmpty(withIl.Instructions);
        Assert.DoesNotContain("Unknown error in reading function IL", withIl.Output.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failed", withIl.Output.Text, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?im)^\s*IL_[0-9a-f]+:", withIl.Output.Text);
    }
}

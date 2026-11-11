// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

public sealed class DynamicMethodTests
{
    public static TheoryData<TestConfig> Matrix => TestMatrices.HeapEnumeration([TargetCatalog.DynamicMethod]);
    public static TheoryData<TestConfig> VariablesMatrix => TestMatrices.StackWalk([TargetCatalog.DynamicMethod], flavor: Flavor.Core, liveness: Liveness.AllValid);

    [SosTheory(SkipTestWithoutData = true)]
    [MemberData(nameof(Matrix))]
    public async Task DumpIl_DecodesEmittedDynamicMethod(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToFirstStop();

        // Runtime helpers also emit DynamicMethods; select the debuggee's Fibonacci body, not the first object.
        DumpIlResult fibonacci = Assert.Single(
            DynamicMethods(target).Select(address => target.DumpIl(address)),
            il => il.Instructions.Count >= 2 && il.Instructions[0].OpCode == "ldarg.0" && il.Instructions[1].OpCode == "ldc.i4.0");
        AssertFibonacciIl(fibonacci);
    }

    [SosTheory(SkipTestWithoutData = true)]
    [MemberData(nameof(VariablesMatrix))]
    public async Task ClrStack_DynamicLocalFeedsDumpIl(TestConfig config)
    {
        SOSTestSkips.SkipICorDebugStackWalk(config);

        using Target target = await Targets.GetTargetAsync(config);
        target.GoToFirstStop();

        IReadOnlyList<TargetExtensions.IcorFrame> frames = target.ClrstackICorDebug(variables: true);
        TargetExtensions.IcorFrame frame = Assert.Single(frames, row => row.CallSite.Contains("GetFibDynamicMethod", StringComparison.Ordinal));
        TargetExtensions.IcorVar local = Assert.Single(frame.Locals, variable => variable.Name == "dynamicMethod");
        Assert.True(local.HasAddress);
        Assert.Contains(local.Address, DynamicMethods(target));
        AssertFibonacciIl(target.DumpIl(local.Address));
    }

    private static IReadOnlyList<ulong> DynamicMethods(Target target)
    {
        SosRow type = target.DumpHeap("-type System.Reflection.Emit.DynamicMethod").Statistics
            .SingleRow(row => row["Class Name"] == "System.Reflection.Emit.DynamicMethod", "the DynamicMethod method table");
        ulong methodTable = type["MT"].AsUInt64(Sos.Addr);
        IReadOnlyList<ulong> objects = target.DumpHeap($"-mt {methodTable:x} -short").ShortAddresses;
        Assert.NotEmpty(objects);
        return objects;
    }

    private static void AssertFibonacciIl(DumpIlResult il)
    {
        Assert.Equal(
            new[] { "ldarg.0", "ldc.i4.0", "bne.un.s", "ldc.i4.0", "ret", "ldarg.0", "ldc.i4.1", "bne.un.s", "ldc.i4.1", "ret", "ldarg.0", "ldc.i4.1", "sub", "call", "ldarg.0", "ldc.i4.2", "sub", "call", "add", "ret" },
            il.Instructions.Select(instruction => instruction.OpCode));
        Assert.Equal(0, il.Instructions[0].Offset);
        Assert.Equal(1, il.Instructions[1].Offset);
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// Exception inspection on the nested-exception crash dump, across the host × flavor matrix. The
/// same assertion body runs under cdb and dotnet-dump, for .NET Core, single-file, and desktop
/// .NET Framework — proving cross-host and cross-flavor equivalence from one test.
/// </summary>
public sealed class PrintExceptionTests
{
    public static TheoryData<TestConfig> NestedMatrix { get; } =
        TestMatrices.CurrentThreadCommands(
            [TargetCatalog.NestedException],
            Liveness.AllValid,
            DumpKind.All);

    public static TheoryData<TestConfig> NoInnerMatrix { get; } =
        TestMatrices.CurrentThreadCommands(
            [TargetCatalog.DivZero, TargetCatalog.SimpleThrow],
            Liveness.AllValid,
            DumpKind.All);

    public static TheoryData<TestConfig> ReflectionMatrix { get; } =
        TestMatrices.CurrentThreadCommands(
            [TargetCatalog.Reflection],
            Liveness.AllValid,
            DumpKind.All);

    [SosTheory]
    [MemberData(nameof(NestedMatrix))]
    public async Task PrintException_Structure(TestConfig config)
    {
        TestMatrices.SkipUnavailableMacOsDotnetDumpThreads(config);

        using Target target = await Targets.GetTargetAsync(config);
        target.GoToFirstStop();

        SosOutput pe = target.Sos("printexception");
        pe["Exception object"].AssertValid(Sos.Addr);
        Assert.NotEmpty(pe["Exception type"].Value);
        Assert.NotEmpty(pe["Message"].Value);
        Assert.NotEmpty(pe["InnerException"].Value); // this is present because we are looking at exception with a nested exception
        pe.AssertContains("StackTrace (generated):");
        AssertFrameSequence(pe, ["NestedExceptionTest.Program.Main"]);

        pe["HResult"].AssertValid(Sos.Hex);
        pe.AssertContains("There are nested exceptions on this thread");

        SosOutput nested = target.Sos("printexception -nested");
        nested.AssertContains("Nested exception");
        nested.AssertContains("System.InvalidOperationException");
        nested.AssertContains("Invalid operation exception, outer");
        nested.AssertContains("System.FormatException");
        nested.AssertContains("Bad format exception, inner");
        Assert.Matches(@"HResult:\s+80131509", nested.Text);
        Assert.Matches(@"HResult:\s+80131537", nested.Text);
        if (nested.Text.Split("NestedExceptionTest.Program.Main").Length - 1 < 2)
        {
            throw nested.Fail("outer and inner NestedExceptionTest.Program.Main frames");
        }

        SosOutput lines = target.Sos("printexception -lines");
        lines.AssertContains("NestedExceptionTest.Program.Main");
        AssertFrameSequence(
            lines,
            ["NestedExceptionTest.Program.Main"],
            ["NestedExceptionTest.cs @ "]);
        if (config.Flavor == Flavor.Framework)
        {
            Assert.Matches(@"NestedExceptionTest\.cs @ (11|20)", lines.Text);
        }
        else
        {
            lines.AssertContains("NestedExceptionTest.cs @ 20");
        }
    }

    [SosTheory]
    [MemberData(nameof(NestedMatrix))]
    public async Task PrintException_Data(TestConfig config)
    {
        TestMatrices.SkipUnavailableMacOsDotnetDumpThreads(config);

        using Target target = await Targets.GetTargetAsync(config);
        target.GoToFirstStop();

        SosOutput pe = target.Sos("printexception");
        Assert.Equal("System.InvalidOperationException", pe["Exception type"]);
        Assert.Equal("Invalid operation exception, outer", pe["Message"]);
        pe["InnerException"].AssertContains("System.FormatException");

        AssertFrameSequence(pe, ["NestedExceptionTest.Program.Main"]);

        Assert.Equal(0x80131509u, pe.Field("HResult").AsUInt32(Sos.Hex));

        ulong innerExceptionAddr = pe["InnerException"].Extract(Sos.Addr);
        SosOutput inner = target.Sos($"printexception {innerExceptionAddr:x}");
        Assert.Equal("System.FormatException", inner["Exception type"]);
        Assert.Equal("Bad format exception, inner", inner["Message"]);
        Assert.Equal("<none>", inner["InnerException"]);
        Assert.Equal(0x80131537u, inner.Field("HResult").AsUInt32(Sos.Hex));
        AssertFrameSequence(inner, ["NestedExceptionTest.Program.Main"]);
    }

    [SosTheory]
    [MemberData(nameof(NoInnerMatrix))]
    public async Task PrintException_NoInnerException(TestConfig config)
    {
        TestMatrices.SkipUnavailableMacOsDotnetDumpThreads(config);

        using Target target = await Targets.GetTargetAsync(config);
        target.GoToFirstStop();

        (string exceptionType, string message, uint hresult, string[] functions, string[] sourceLines) = config.Target switch
        {
            TargetCatalog.DivZero => (
                "System.DivideByZeroException",
                "Attempted to divide by zero.",
                0x80020012,
                new[] { "C.DivideByZero", "C.F3", "C.F2", "C.Main" },
                new[]
                {
                    "DivZero.cs @ 15",
                    "DivZero.cs @ 24",
                    "DivZero.cs @ 36",
                    config.Flavor == Flavor.Framework ? "DivZero.cs @ 56" : "DivZero.cs @ 57",
                }),
            TargetCatalog.SimpleThrow => (
                "System.InvalidOperationException",
                "Throwing an invalid operation....",
                0x80131509,
                new[] { "UserObject.UseObject", "Simple.Main" },
                new[] { "UserObject.cs @ 19", "SimpleThrow.cs @ 12" }),
            _ => throw new ArgumentOutOfRangeException(nameof(config), config.Target, "unexpected target"),
        };

        SosOutput pe = target.Sos("printexception");
        Assert.Equal(exceptionType, pe["Exception type"]);
        Assert.Equal(message, pe["Message"]);
        Assert.Equal("<none>", pe["InnerException"]);
        Assert.Equal(hresult, pe.Field("HResult").AsUInt32(Sos.Hex));
        AssertFrameSequence(pe, functions);

        SosOutput nested = target.Sos("printexception -nested");
        Assert.Equal(exceptionType, nested["Exception type"]);
        Assert.Equal(message, nested["Message"]);
        Assert.Equal("<none>", nested["InnerException"]);
        Assert.Equal(hresult, nested.Field("HResult").AsUInt32(Sos.Hex));
        AssertFrameSequence(nested, functions);

        SosOutput lines = target.Sos("printexception -lines");
        Assert.Equal(exceptionType, lines["Exception type"]);
        Assert.Equal(message, lines["Message"]);
        Assert.Equal("<none>", lines["InnerException"]);
        Assert.Equal(hresult, lines.Field("HResult").AsUInt32(Sos.Hex));
        AssertFrameSequence(lines, functions, sourceLines);
    }

    [SosTheory]
    [MemberData(nameof(ReflectionMatrix))]
    public async Task PrintException_ReflectionInnerException(TestConfig config)
    {
        TestMatrices.SkipUnavailableMacOsDotnetDumpThreads(config);

        using Target target = await Targets.GetTargetAsync(config);
        target.GoToFirstStop();

        SosOutput pe = target.Sos("printexception");
        Assert.Equal("System.Reflection.TargetInvocationException", pe["Exception type"]);
        Assert.Equal("Exception has been thrown by the target of an invocation.", pe["Message"]);
        pe["InnerException"].AssertContains("System.Exception");
        Assert.Equal(0x80131604u, pe.Field("HResult").AsUInt32(Sos.Hex));
        pe.Table(("SP", Sos.Addr), ("IP", Sos.Addr), ("Function", Sos.ModuleFunctionWithOffset))
            .AssertContainsRow(row => row["Function"].Contains("RefLoader.Loader.Main"), "Function contains RefLoader.Loader.Main");

        ulong innerExceptionAddr = pe["InnerException"].Extract(Sos.Addr);
        SosOutput inner = target.Sos($"printexception {innerExceptionAddr:x}");
        Assert.Equal("System.Exception", inner["Exception type"]);
        Assert.Equal("Exception from InvokedCode.Invoked.ExceptionNoHandler()", inner["Message"]);
        Assert.Equal(0x80131500u, inner.Field("HResult").AsUInt32(Sos.Hex));

        SosOutput nested = target.Sos("printexception -nested");
        nested.AssertContains("System.Reflection.TargetInvocationException");
        nested.AssertContains("System.Exception");

        SosOutput lines = target.Sos("printexception -lines");
        AssertFrameSequence(lines, ["RefLoader.Loader.Main"], ["ReflectionTest.cs @ "]);
    }

    private static void AssertFrameSequence(
        SosOutput output,
        IReadOnlyList<string> expectedFunctions,
        IReadOnlyList<string>? expectedSourceLines = null)
    {
        if (expectedSourceLines is not null && expectedSourceLines.Count != expectedFunctions.Count)
        {
            throw new ArgumentException("Source-line expectations must match the function count.", nameof(expectedSourceLines));
        }

        SosTable table = output.Table("SP", "IP", "Function");
        int searchFrom = 0;
        for (int expectedIndex = 0; expectedIndex < expectedFunctions.Count; expectedIndex++)
        {
            string expectedFunction = expectedFunctions[expectedIndex];
            int row = -1;
            for (int i = searchFrom; i < table.Length; i++)
            {
                if (table.Row(i)["Function"].Value.Contains(expectedFunction, StringComparison.Ordinal))
                {
                    row = i;
                    break;
                }
            }

            if (row < 0)
            {
                throw output.Fail($"ordered stack frame '{expectedFunction}' at or after row {searchFrom}");
            }

            SosRow frame = table.Row(row);
            if (!Sos.Addr.Matches(frame["SP"].Value) || !Sos.Addr.Matches(frame["IP"].Value))
            {
                throw output.Fail($"frame '{expectedFunction}' to contain valid SP and IP addresses");
            }

            string functionCell = frame["Function"].Value;
            int sourceStart = functionCell.LastIndexOf(" [", StringComparison.Ordinal);
            string function = sourceStart >= 0 ? functionCell[..sourceStart] : functionCell;
            if (!Sos.ModuleFunctionWithOffset.Matches(function))
            {
                throw output.Fail($"frame '{expectedFunction}' to contain a module, function, and offset");
            }
            if (expectedSourceLines is not null &&
                !functionCell.Contains(expectedSourceLines[expectedIndex], StringComparison.Ordinal))
            {
                throw output.Fail(
                    $"frame '{expectedFunction}' to contain source annotation '{expectedSourceLines[expectedIndex]}'");
            }

            searchFrom = row + 1;
        }
    }
}

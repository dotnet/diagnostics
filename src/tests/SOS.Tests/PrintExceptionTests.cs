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
        TestConfig.BuildMatrix(
            [TargetCatalog.NestedException],
            liveness: Liveness.AllValid,
            dumpKind: DumpKind.All);

    public static TheoryData<TestConfig> NoInnerMatrix { get; } =
        TestConfig.BuildMatrix(
            [TargetCatalog.DivZero, TargetCatalog.SimpleThrow],
            liveness: Liveness.AllValid,
            dumpKind: DumpKind.All);

    public static TheoryData<TestConfig> ReflectionMatrix { get; } =
        TestConfig.BuildMatrix(
            [TargetCatalog.Reflection],
            liveness: Liveness.AllValid,
            dumpKind: DumpKind.All);

    [SosTheory]
    [MemberData(nameof(NestedMatrix))]
    public async Task PrintException_Structure(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToFirstStop();

        SosOutput pe = target.Sos("printexception");
        pe["Exception object"].AssertValid(Sos.Addr);
        Assert.NotEmpty(pe["Exception type"].Value);
        Assert.NotEmpty(pe["Message"].Value);
        Assert.NotEmpty(pe["InnerException"].Value); // this is present because we are looking at exception with a nested exception
        pe.AssertContains("StackTrace (generated):");

        SosTable table = pe.Table(("SP", Sos.Addr), ("IP", Sos.Addr), ("Function", Sos.ModuleFunctionWithOffset));
        Assert.NotEmpty(table);

        pe["HResult"].AssertValid(Sos.Hex);
        pe.AssertContains("There are nested exceptions on this thread");

        SosOutput nested = target.Sos("printexception -nested");
        nested.AssertContains("Nested exception");
        nested.AssertContains("System.InvalidOperationException");
        nested.AssertContains("Invalid operation exception, outer");
        nested.AssertContains("System.FormatException");
        nested.AssertContains("Bad format exception, inner");

        SosOutput lines = target.Sos("printexception -lines");
        lines.AssertContains("NestedExceptionTest.Program.Main");
        if (config.Host != Host.Lldb)
        {
            lines.AssertContains("NestedExceptionTest.cs");
        }
    }

    [SosTheory]
    [MemberData(nameof(NestedMatrix))]
    public async Task PrintException_Data(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToFirstStop();

        SosOutput pe = target.Sos("printexception");
        Assert.Equal("System.InvalidOperationException", pe["Exception type"]);
        Assert.Equal("Invalid operation exception, outer", pe["Message"]);
        pe["InnerException"].AssertContains("System.FormatException");

        SosTable table = pe.Table(("SP", Sos.Addr), ("IP", Sos.Addr), ("Function", Sos.ModuleFunctionWithOffset));
        table.AssertContainsRow(row => row["Function"].Contains("NestedExceptionTest.Program.Main"), "Function contains NestedExceptionTest.Program.Main");

        Assert.Equal(0x80131509u, pe.Field("HResult").AsUInt32(Sos.Hex));

        ulong innerExceptionAddr = pe["InnerException"].Extract(Sos.Addr);
        SosOutput inner = target.Sos($"printexception {innerExceptionAddr:x}");
        Assert.Equal("System.FormatException", inner["Exception type"]);
        Assert.Equal("Bad format exception, inner", inner["Message"]);
    }

    [SosTheory]
    [MemberData(nameof(NoInnerMatrix))]
    public async Task PrintException_NoInnerException(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToFirstStop();

        (string exceptionType, string message, uint hresult, string function, string sourceFile) = config.Target switch
        {
            TargetCatalog.DivZero => (
                "System.DivideByZeroException",
                "Attempted to divide by zero.",
                0x80020012,
                "C.DivideByZero",
                "DivZero.cs"),
            TargetCatalog.SimpleThrow => (
                "System.InvalidOperationException",
                "Throwing an invalid operation....",
                0x80131509,
                "UserObject.UseObject",
                "UserObject.cs"),
            _ => throw new ArgumentOutOfRangeException(nameof(config), config.Target, "unexpected target"),
        };

        SosOutput pe = target.Sos("printexception");
        Assert.Equal(exceptionType, pe["Exception type"]);
        Assert.Equal(message, pe["Message"]);
        Assert.Equal("<none>", pe["InnerException"]);
        Assert.Equal(hresult, pe.Field("HResult").AsUInt32(Sos.Hex));
        SosTable table = pe.Table(("SP", Sos.Addr), ("IP", Sos.Addr), ("Function", Sos.ModuleFunctionWithOffset));
        table.AssertContainsRow(row => row["Function"].Contains(function), $"Function contains {function}");

        SosOutput nested = target.Sos("printexception -nested");
        Assert.Equal(exceptionType, nested["Exception type"]);
        Assert.Equal("<none>", nested["InnerException"]);

        SosOutput lines = target.Sos("printexception -lines");
        lines.AssertContains(function);
        if (config.Host != Host.Lldb)
        {
            lines.AssertContains(sourceFile);
        }
    }

    [SosTheory]
    [MemberData(nameof(ReflectionMatrix))]
    public async Task PrintException_ReflectionInnerException(TestConfig config)
    {
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
        lines.AssertContains("RefLoader.Loader.Main");
        if (config.Host != Host.Lldb)
        {
            lines.AssertContains("ReflectionTest.cs");
        }
    }
}

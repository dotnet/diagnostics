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
    /// <summary>
    /// A matrix of all combinations of hosts, targets, and flavors.
    /// Hosts.DumpHosts = [cdb, dotnet-dump] || [lldb, dotnet-dump]
    /// Flavors = e.g. [Flavor.Core, Flavor.SingleFile, Flavor.Framework]
    /// </summary>
    public static TheoryData<TestConfig> Matrix { get; } =
        TestConfig.BuildMatrix([TargetCatalog.NestedException], dumpKind: DumpKind.All);


    [SosTheory]
    [MemberData(nameof(Matrix))]
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
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
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
}

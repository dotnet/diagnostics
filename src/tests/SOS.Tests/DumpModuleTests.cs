// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// <c>!dumpmodule</c> (and <c>!dumpmodule -mt</c>) on the scenarios debuggee across the full matrix. The
/// module address is discovered through <c>dumpdomain</c> (so the test never hard-codes an address), then
/// the structure test asserts the module's fields and round-trips its <c>Assembly</c> pointer back to the
/// assembly <c>dumpdomain</c> reported. The types test asserts the <c>-mt</c> "Types defined" table actually
/// contains the debuggee's uniquely-named public marker types (not just "the table is non-empty"), each
/// with a real method table and metadata token — improving on the legacy script, which only checked that a
/// couple of module fields were hex.
/// </summary>
public sealed class DumpModuleTests
{
    public static TheoryData<TestConfig> Matrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios]);

    // Uniquely-named public types the debuggee defines; stable across flavors (same source).
    private static readonly string[] s_markerTypes =
    {
        "SosHarnessScenarios", "ThinLockMarker", "LiveUniqueMarker", "DeadUniqueMarker",
    };

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task DumpModule_Structure(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        (AssemblyInfo assembly, ModuleRef module) = FindDebuggeeModule(target, config.Flavor);
        DumpModuleResult dump = target.DumpModule(module.Address);

        // dumpmodule's Name carries the same module file dumpdomain reported (single-file prints just the
        // file name, on-disk flavors the full path), and its Assembly pointer round-trips to dumpdomain.
        Assert.EndsWith(System.IO.Path.GetFileName(module.Path), dump.Name, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(assembly.Address, dump.Assembly);
        Assert.NotEqual(0ul, dump.BaseAddress);
        Assert.NotEqual(0ul, dump.TypeDefToMethodTableMap);
        Assert.NotEqual(0ul, dump.MetaData.Start);
        Assert.True(dump.MetaData.Size > 0, $"metadata size should be positive (was {dump.MetaData.Size})");
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task DumpModule_Types(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        (_, ModuleRef module) = FindDebuggeeModule(target, config.Flavor);
        DumpModuleResult dump = target.DumpModule(module.Address, includeTypes: true);

        Assert.NotEmpty(dump.TypesDefined);   // guard: -mt must have actually produced the table
        Assert.NotEmpty(dump.TypesReferenced);
        Assert.Contains(dump.TypesReferenced, t => t.Name == "System.Object");

        // Every uniquely-named marker type the debuggee defines is present, with a real MT + token.
        foreach (string typeName in s_markerTypes)
        {
            TypeEntry type = dump.DefinedType(typeName);
            Assert.NotEqual(0ul, type.MethodTable);
            Assert.NotEqual(0u, type.Token);
        }
    }

    private static (AssemblyInfo Assembly, ModuleRef Module) FindDebuggeeModule(Target target, Flavor flavor)
    {
        DumpDomainResult domains = target.DumpDomain();
        string moduleName = TargetCatalog.Get(TargetCatalog.Scenarios).ModuleFor(flavor);
        AssemblyInfo assembly = domains.FindAssemblyByPathSuffix(moduleName)
            ?? throw new Xunit.Sdk.XunitException($"dumpdomain did not list the debuggee assembly '{moduleName}':\n{domains.Output.Text}");
        ModuleRef module = assembly.Modules.Single(m => m.Path.EndsWith(moduleName, StringComparison.OrdinalIgnoreCase));
        return (assembly, module);
    }
}

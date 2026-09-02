// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// <c>!name2ee</c> and <c>!token2ee</c> on the scenarios debuggee across the full matrix. Both commands
/// resolve a module-scoped name/token to the same EE structures, so the test drives them off the
/// <c>dumpmodule -mt</c> type table (the source of truth for a type's method table + metadata token) and
/// asserts the two resolvers agree with each other and with the table — a cross-command round-trip the
/// legacy scripts never did (they had no <c>token2ee</c> coverage at all). A second case resolves a public
/// method that is on the stack at the stop, exercising the method shape (MethodDesc + JITTED Code Address).
/// </summary>
public sealed class ModuleResolveTests
{
    public static TheoryData<TestConfig> Matrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios]);

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task Name2EE_And_Token2EE_AgreeOnType(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        string moduleName = TargetCatalog.Get(TargetCatalog.Scenarios).ModuleFor(config.Flavor);
        DumpDomainResult domains = target.DumpDomain();
        AssemblyInfo assembly = domains.FindAssemblyByPathSuffix(moduleName)
            ?? throw new Xunit.Sdk.XunitException($"dumpdomain did not list the debuggee assembly '{moduleName}':\n{domains.Output.Text}");
        ModuleRef module = assembly.Modules.Single(m => m.Path.EndsWith(moduleName, StringComparison.OrdinalIgnoreCase));

        // Source of truth: the type's row in dumpmodule -mt (uniquely-named type => single row).
        TypeEntry marker = target.DumpModule(module.Address, includeTypes: true).DefinedType("ThinLockMarker");

        // token2ee <module> <token> resolves to the same module/MT/name.
        EEMatch byToken = target.Token2EE(moduleName, marker.Token).Single;
        Assert.Equal(marker.Token, byToken.Token);
        Assert.Equal(marker.MethodTable, byToken.MethodTable);
        Assert.Equal("ThinLockMarker", byToken.Name);

        // name2ee <module>!<type> resolves to the same module/MT/token/name.
        EEResult byName = target.Name2EE($"{moduleName}!ThinLockMarker");
        Assert.Equal(module.Address, byName.Module);
        EEMatch named = byName.Single;
        Assert.Equal(marker.MethodTable, named.MethodTable);
        Assert.Equal(marker.Token, named.Token);
        Assert.Equal("ThinLockMarker", named.Name);
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task Name2EE_ResolvesMethodOnStack(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        string moduleName = TargetCatalog.Get(TargetCatalog.Scenarios).ModuleFor(config.Flavor);

        // AtHeap() is the public method that raises the heap stop, so it is jitted and on the stack here.
        EEMatch method = target.Name2EE($"{moduleName}!SosHarnessScenarios.AtHeap").Single;
        Assert.Contains("SosHarnessScenarios.AtHeap", method.Name);
        Assert.NotNull(method.MethodDesc);
        Assert.NotEqual(0ul, method.MethodDesc!.Value);
        Assert.NotNull(method.JittedCodeAddress); // present because the method is jitted at this stop
        Assert.NotEqual(0ul, method.JittedCodeAddress!.Value);
    }
}

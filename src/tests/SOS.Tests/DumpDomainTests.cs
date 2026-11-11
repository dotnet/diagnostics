// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// <c>!dumpdomain</c> and <c>!dumpassembly</c> on the scenarios debuggee, across the full Host × Flavor ×
/// Liveness matrix. The structure test asserts the domain shape — including the parts that differ by
/// runtime: desktop .NET Framework has a <c>Shared Domain</c> (which .NET Core lacks) and names its default
/// domain after the entry-point exe — and that the debuggee's own assembly/module are present. The
/// round-trip test then takes the assembly address straight out of <c>dumpdomain</c>, feeds it to
/// <c>dumpassembly</c>, and asserts the two agree exactly (parent domain, name, module) rather than the
/// legacy script's "is it a hex value".
/// </summary>
public sealed class DumpDomainTests
{
    public static TheoryData<TestConfig> Matrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios]);

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task DumpDomain_Structure(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        DumpDomainResult domains = target.DumpDomain();

        // System Domain block. .NET 11 intentionally stopped emitting it: the DAC's
        // GetAppDomainStoreData now hardcodes systemDomain == 0 (there is no separate System Domain object
        // to print), so dumpdomain prints nothing for it. On .NET Framework and .NET Core <= 10 the block is
        // present with no user assemblies and real heap pointers.
        if (config.Flavor != Flavor.Framework && config.CoreVersion >= CoreVersion.Net11)
        {
            Assert.DoesNotContain(domains.Domains, d => d.Kind == DomainKind.System);
        }
        else
        {
            DomainInfo system = domains.SystemDomain;
            Assert.NotEqual(0ul, system.Address);
            Assert.NotEqual(0ul, system.LowFrequencyHeap);
            Assert.NotEqual(0ul, system.HighFrequencyHeap);
            Assert.NotEqual(0ul, system.StubHeap);
        }

        // There is at least one application domain, and the debuggee's own assembly + module are loaded.
        Assert.NotEmpty(domains.AppDomains);
        string moduleName = TargetCatalog.Get(TargetCatalog.Scenarios).ModuleFor(config.Flavor);
        AssemblyInfo debuggee = domains.FindAssemblyByPathSuffix(moduleName)
            ?? throw new Xunit.Sdk.XunitException($"dumpdomain did not list the debuggee assembly '{moduleName}':\n{domains.Output.Text}");
        Assert.NotEqual(0ul, debuggee.Address);
        Assert.Contains(debuggee.Modules, m => m.Path.EndsWith(moduleName, StringComparison.OrdinalIgnoreCase));

        // Runtime-specific domain shape. Desktop .NET Framework has a Shared Domain (domain-neutral
        // assemblies such as mscorlib) and names its default domain after the exe; .NET Core (incl.
        // single-file) has neither.
        if (config.Flavor == Flavor.Framework)
        {
            DomainInfo shared = domains.SharedDomain
                ?? throw new Xunit.Sdk.XunitException($"desktop dumpdomain should have a Shared Domain:\n{domains.Output.Text}");
            Assert.NotEqual(0ul, shared.Address);
            Assert.Contains(domains.AppDomains, d => d.Name.EndsWith(moduleName, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            Assert.Null(domains.SharedDomain);
        }
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task DumpDomain_AssemblyRoundTrip(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        DumpDomainResult domains = target.DumpDomain();
        string moduleName = TargetCatalog.Get(TargetCatalog.Scenarios).ModuleFor(config.Flavor);
        AssemblyInfo debuggee = domains.FindAssemblyByPathSuffix(moduleName)
            ?? throw new Xunit.Sdk.XunitException($"dumpdomain did not list the debuggee assembly '{moduleName}':\n{domains.Output.Text}");
        DomainInfo owner = domains.Domains.Single(d => d.Assemblies.Any(a => a.Address == debuggee.Address));

        // Feed the assembly address from dumpdomain into dumpassembly; the two views must agree exactly.
        DumpAssemblyResult assembly = target.DumpAssembly(debuggee.Address);
        Assert.Equal(owner.Address, assembly.ParentDomain);

        // On-disk flavors carry the assembly's full path as its Name in both commands. Single-file bundles
        // have no on-disk assembly, so dumpdomain prints an empty path while dumpassembly prints "Unknown";
        // there the module round-trip below is the identity check instead.
        if (debuggee.Path.Length > 0)
        {
            Assert.Equal(debuggee.Path, assembly.Name);
        }

        ModuleRef expectedModule = Assert.Single(debuggee.Modules);
        ModuleRef actualModule = Assert.Single(assembly.Modules);
        Assert.Equal(expectedModule.Address, actualModule.Address);
        Assert.Equal(expectedModule.Path, actualModule.Path);
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// The object/type inspection chain — <c>!dumpobj</c>/<c>do</c>, <c>!dumpmt</c> (+ <c>-MD</c>/<c>-all</c>),
/// <c>!dumpclass</c>, <c>!dumpmd</c>, and <c>!ip2md</c> — across the full Host × Flavor × Liveness matrix.
/// Everything is anchored on a single uniquely-typed marker object and a public method that is on the stack
/// at the stop, then the commands are chained so each one's output feeds the next and the shared identity
/// (method table, metadata token, method desc) is asserted to be consistent across all of them.
/// </summary>
public sealed class ObjectInspectionTests
{
    public static TheoryData<TestConfig> Matrix => TestMatrices.FullDumpOnCoreVersions([TargetCatalog.Scenarios], CoreVersion.Net8 | CoreVersion.Net9 | CoreVersion.Net10);

    public static TheoryData<TestConfig> DumpObjChainMatrix { get; } = BuildDumpObjChainMatrix();

    public static TheoryData<TestConfig> DumpObjNoFieldsMatrix { get; } = TestMatrices.CoreFrameworkConditional([TargetCatalog.Scenarios]);

    // Live opt-in: !dumpobj reads an object's fields straight from live process memory, so the base
    // dumpobj/dumpmt/dumpmd chain runs dump AND live as the representative live-object-read check. The
    // other inspection methods here (-nofields, ip2md) stay dump-only.
    public static TheoryData<TestConfig> LiveMatrix => TestConfig.BuildMatrix([TargetCatalog.Scenarios], liveness: Liveness.AllValid);

    private static TheoryData<TestConfig> BuildDumpObjChainMatrix()
    {
        TheoryData<TestConfig> data = new();

        foreach (TestConfig config in TestConfig.Permutations([TargetCatalog.Scenarios], liveness: Liveness.Live))
        {
            data.Add(config);
        }

        foreach (TestConfig config in TestMatrices.CoreFrameworkConditionalFullDumpConfigs([TargetCatalog.Scenarios]))
        {
            data.Add(config);
        }

        return data;
    }

    [SosTheory]
    [MemberData(nameof(DumpObjChainMatrix))]
    public async Task DumpObj_Mt_Class_Md_Chain(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        // Use Full dumps for the dump half of this object-data chain because the net10 legacy DAC can
        // crash while servicing dumpobj's optional ComWrappers data query on reduced Heap dumps. Live rows
        // still exercise live object reads; the rest of the chain is independent of dump reduction.
        target.GoToStopPoint(TargetCatalog.StopHeap);

        ulong marker = target.FindUniqueObject("ThinLockMarker");

        // dumpobj: identity of the object.
        DumpObjResult obj = target.DumpObj(marker);
        Assert.Equal("ThinLockMarker", obj.Name);
        Assert.Equal(24, obj.Size);
        ulong mt = obj.MethodTable;
        Assert.NotEqual(0ul, mt);

        // dumpmt -MD: the type's method table and its method slot list (Object's virtuals + the ctor).
        DumpMtResult dumpMt = target.DumpMt(mt, methods: true);
        Assert.Equal("ThinLockMarker", dumpMt.Name);
        Assert.NotEmpty(dumpMt.Methods);
        Assert.Equal(dumpMt.NumberOfMethods, dumpMt.Methods.Count);
        MethodSlot ctor = dumpMt.Methods.First(m => m.Name.Contains("..ctor", StringComparison.Ordinal));

        // dumpmt -all: same table plus the "Additional Details" field counts.
        DumpMtResult dumpMtAll = target.DumpMt(mt, all: true);
        Assert.Equal(0, dumpMtAll.NumInstanceFields); // ThinLockMarker has no fields
        Assert.Equal(0, dumpMtAll.NumStaticFields);

        // dumpclass: desktop .NET Framework needs the EEClass (from dumpmt); modern .NET accepts the MT.
        DumpClassResult dumpClass = target.DumpClass(dumpMt.EEClass ?? mt);
        Assert.Equal("ThinLockMarker", dumpClass.ClassName);
        Assert.Equal(mt, dumpClass.MethodTable);
        Assert.Equal(dumpMt.MdToken, dumpClass.MdToken);

        // dumpmd <MethodDesc>: the ctor resolves back to this same method table.
        MethodDumpResult dumpMd = target.DumpMd(ctor.MethodDesc);
        Assert.Contains("ThinLockMarker..ctor", dumpMd.MethodName, StringComparison.Ordinal);
        Assert.Equal(mt, dumpMd.MethodTable);

        // do is an alias for dumpobj, asserted last: the alias collides with lldb's built-in 'do' command
        // so it can't be dispatched through the lldb SOS host. Return early there rather than skipping —
        // everything above has already been verified on this config.
        if (config.Host == Host.Lldb)
        {
            return;
        }

        target.Sos($"do {marker:x}").AssertContains("ThinLockMarker");
    }

    [SosTheory]
    [MemberData(nameof(DumpObjNoFieldsMatrix))]
    public async Task DumpObj_NoFields_OmitsFieldTable(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        // Use Full dumps for this object-data test because the net10 legacy DAC can crash while servicing
        // dumpobj's optional ComWrappers data query on reduced Heap dumps. The command's -nofields behavior
        // is independent of dump reduction, so keep this test on Full Core/Framework dumps instead of
        // excluding the failing Core row.
        target.GoToStopPoint(TargetCatalog.StopHeap);

        ulong marker = target.FindUniqueObject("FieldMarker");
        DumpObjResult full = target.DumpObj(marker);
        Assert.NotEmpty(full.Fields);

        DumpObjResult noFields = target.DumpObj(marker, noFields: true);
        Assert.Empty(noFields.Fields);          // -nofields drops the Fields table
        Assert.Equal(full.Name, noFields.Name); // but the scalar identity is unchanged
        Assert.Equal(full.MethodTable, noFields.MethodTable);
    }

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task Ip2md_ResolvesJittedMethodWithSource(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(TargetCatalog.StopHeap);

        // AtHeap() raises the heap stop, so it is jitted; name2ee gives us its native entry point.
        string moduleName = TargetCatalog.Get(TargetCatalog.Scenarios).ModuleFor(config.Flavor);
        EEMatch atHeap = target.Name2EE($"{moduleName}!SosHarnessScenarios.AtHeap").Single;
        Assert.NotNull(atHeap.JittedCodeAddress);

        MethodDumpResult ip2md = target.Ip2md(atHeap.JittedCodeAddress!.Value);
        Assert.Equal("SosHarnessScenarios.AtHeap()", ip2md.MethodName);
        Assert.True(ip2md.IsJitted);
        Assert.Equal(atHeap.MethodDesc, ip2md.MethodDesc); // ip2md's MethodDesc == name2ee's
        Assert.Equal("SosHarnessScenarios.cs", Path.GetFileName(ip2md.SourceFile));
        Assert.True(ip2md.SourceLine is > 0, $"expected a positive source line, got {ip2md.SourceLine?.ToString() ?? "<none>"}");
    }
}

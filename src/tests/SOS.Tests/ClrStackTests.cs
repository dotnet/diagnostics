// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// Coverage for <c>!clrstack -r</c> (register display). The legacy SOS scripts only verified the
/// <em>shape</em> of the output (the "OS Thread Id" banner, the "Child SP / IP / Call Site" header,
/// and that each frame prints a register block). We assert the shape too (see
/// <see cref="TargetExtensions.ClrstackRegisters"/>), then go further and check <em>values</em>:
/// SOS fills the table's Child SP / IP columns from the same per-frame register context it dumps
/// (strike.cpp <c>GetFrameLocation</c> / <c>PrintManagedFrameContext</c> read one context with
/// <c>GetFullContextFlags</c>). So for every frame the IP column must equal the instruction-pointer
/// register (rip/eip/pc), and for every real (non-internal) managed frame the Child SP column must
/// equal the stack-pointer register (rsp/esp/sp). Internal clr!Frames report the Frame address as
/// Child SP instead of the context SP, so only their IP is matched.
/// </summary>
public sealed class ClrStackTests
{
    // Instruction-pointer / stack-pointer register names across the platforms SOS prints in
    // strike.cpp PrintManagedFrameContext: amd64 rip/rsp, x86 eip/esp, arm & arm64 pc/sp.
    private static readonly string[] s_ipRegisters = ["rip", "eip", "pc"];
    private static readonly string[] s_spRegisters = ["rsp", "esp", "sp"];

    // The legacy !clrstack -r scripts ran against NestedExceptionTest (StackTests.script),
    // SymbolTestApp (StackAndOtherTests.script), and WebApp3 (WebApp.script). SymbolTestApp and
    // WebApp3 are deferred (multi-assembly / ASP.NET+DualRuntimes). Since -r is target-agnostic, we
    // exercise it over a diverse set of already-ported debuggees with deliberately different stack
    // shapes / stop kinds, so the SP/IP <-> register invariant is checked across varied frames:
    //   NestedException        - unhandled exception crash (the real -r debuggee)
    //   DivZero                - hardware fault (div by zero), deep non-async call chain
    //   AsyncMain              - async state-machine frames
    //   DynamicMethod          - a dynamic (IL-emitted) method on the stack
    //   Scenarios              - snapshot (marker) stop rather than a crash
    public static TheoryData<TestConfig> RegistersMatrix { get; }
        = TestMatrices.StackWalk(
            [
                TargetCatalog.NestedException,
                TargetCatalog.DivZero,
                TargetCatalog.AsyncMain,
                TargetCatalog.DynamicMethod,
                TargetCatalog.Scenarios,
            ],
            // Live opt-in: !clrstack is one of the few commands that genuinely exercises a different path
            // live (it unwinds a live thread's stack and reads its register context), so this base
            // stackwalk runs dump AND live. Most other clrstack variations stay dump-only.
            liveness: Liveness.AllValid);

    [SosTheory]
    [MemberData(nameof(RegistersMatrix))]
    public async Task ClrStack_Registers(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToFirstStop();

        SosTable table = target.ClrstackRegisters();

        bool sawManagedFrame = false;
        foreach (SosRow row in table)
        {
            // The IP column is filled from the frame context's instruction pointer, which -r also
            // dumps as a register column, so they must agree for every frame (internal frames included).
            ulong ip = row["IP"].AsUInt64(Sos.Addr);
            ulong ipRegister = Register(row, s_ipRegisters);
            Assert.Equal(ip, ipRegister);

            // Internal clr!Frames report the Frame address as Child SP (not the context SP), so only
            // real managed frames have a Child SP that equals the stack-pointer register.
            bool internalFrame = row["InternalFrame"].AsBoolean();
            if (!internalFrame)
            {
                sawManagedFrame = true;
                ulong sp = row["Child SP"].AsUInt64(Sos.Addr);
                ulong spRegister = Register(row, s_spRegisters);
                Assert.Equal(sp, spRegister);
            }
        }

        Assert.True(sawManagedFrame, "clrstack -r produced no non-internal managed frame to match Child SP against.");
    }

    // The value of the row's register column whose name is one of <paramref name="names"/>.
    private static ulong Register(SosRow row, string[] names)
    {
        string? name = names.FirstOrDefault(row.HasColumn);
        Assert.NotNull(name);
        return row[name!].AsUInt64(Sos.Addr);
    }

    // !clrstack -gc is not covered by the legacy scripts at all. It must be exercised over stops that
    // hold object references in ordinary live locals: whether an object is a GC-reported *stack* root
    // depends on the capture point, so crash/throw targets are unreliable (e.g. an exception is only a
    // -gc root while the runtime's managed dispatch frame is live, which createdump captures but a
    // 2nd-chance/dbgeng capture does not — dso still finds it by scanning stack memory, but -gc
    // legitimately reports nothing). The Scenarios marker stops avoid that; we use two with different
    // root shapes, both BEFORE any GC.Collect so the live path is safe:
    //   roots      - a normal object + a pinned byte[] + an interior int[] ref live across the marker
    //   argslocals - a uniquely-typed reference arg and local live across the marker
    public static TheoryData<TestConfig, string> GcRootsMatrix { get; } = BuildGcRootsMatrix();

    // The stop point is an extra (non-axis) column paired with each config, so this is built by hand from
    // the raw config permutations rather than the single-column BuildMatrix.
    private static TheoryData<TestConfig, string> BuildGcRootsMatrix()
    {
        TheoryData<TestConfig, string> data = new();
        // Live opt-in: !clrstack -gcroots is fundamentally different from !clrstack (it scans the live
        // stack and registers for GC-reported roots), so it runs dump AND live.
        foreach (TestConfig config in TestMatrices.StackWalkConfigs([TargetCatalog.Scenarios], liveness: Liveness.AllValid))
        {
            data.Add(config, TargetCatalog.StopRoots);
            data.Add(config, TargetCatalog.StopArgsLocals);
        }

        return data;
    }

    [SosTheory]
    [MemberData(nameof(GcRootsMatrix))]
    public async Task ClrStack_GcRoots(TestConfig config, string stopName)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint(stopName);

        // The objects dumpstackobjects finds by scanning stack memory.
        SosTable dsoTable = target.DumpStackObjects();
        HashSet<ulong> dsoObjects = dsoTable.Select(r => r["Object"].AsUInt64(Sos.Addr)).ToHashSet();

        SosTable gc = target.ClrstackGcRoots();

        int compared = 0;
        foreach (SosRow frame in gc)
        {
            foreach (SosDataRow root in frame.Data)
            {
                // Pinned / Interior are always present (default False even when not printed).
                bool interior = root["Interior"].AsBoolean();
                _ = root["Pinned"].AsBoolean();
                ulong obj = root["Object"].AsUInt64(Sos.Addr);

                // A non-interior, non-null root that lives in a stack slot holds an object pointer in
                // stack memory, so dumpstackobjects (which scans that memory) must have found it too.
                // Interior pointers don't point at an object head, so dso won't list them — skip them.
                if (interior || obj == 0 || root["Address"].Value.Length == 0)
                    continue;

                Assert.Contains(obj, dsoObjects);
                compared++;
            }
        }

        Assert.True(compared > 0, "clrstack -gc produced no non-interior stack-slot roots to compare against dumpstackobjects.");
    }

    // GcRoots is the dedicated target that deliberately keeps one of each root flavor alive across
    // the marker, so the parser's handling of the optional (pinned)/(interior) flags and the
    // sometimes-absent type is actually exercised — and so we can assert the SosDataRow always
    // carries Pinned/Interior (defaulting to False when the flag isn't printed).
    public static TheoryData<TestConfig> GcRootsFlagMatrix { get; } = TestMatrices.StackWalkFullDumpOnCoreVersions([TargetCatalog.Scenarios], CoreVersion.Net8 | CoreVersion.Net9 | CoreVersion.Net10);

    [SosTheory]
    [MemberData(nameof(GcRootsFlagMatrix))]
    public async Task ClrStack_GcRoots_Flags(TestConfig config)
    {
        using Target target = await Targets.GetTargetAsync(config);
        target.GoToStopPoint("roots");

        SosTable gc = target.ClrstackGcRoots();
        List<SosDataRow> roots = gc.SelectMany(f => f.Data).ToList();
        Assert.NotEmpty(roots);

        // Every record always carries both flag fields (False when SOS didn't print the flag).
        Assert.All(roots, r =>
        {
            Assert.True(r.Has("Pinned"));
            Assert.True(r.Has("Interior"));
        });

        // The fixed byte[] is a pinned root: (pinned), not interior, and SOS still prints its type
        // (it points at the object head, so it isn't interior).
        SosDataRow pinned = roots.AssertSingle(r => r["Pinned"].AsBoolean(), "a pinned root");
        Assert.False(pinned["Interior"].AsBoolean());
        Assert.NotEqual(0ul, pinned["Object"].AsUInt64(Sos.Addr));
        Assert.Contains("Byte[]", pinned["Type"].Value);

        // The ref into the int[] is an interior root: (interior), and SOS prints no type for it
        // (an interior pointer doesn't point at an object head).
        roots.AssertContains(r => r["Interior"].AsBoolean() && r["Type"].Value.Length == 0, "an interior root with no Type");

        // A normal object root: neither flag set, and a type is present.
        roots.AssertContains(
            r => !r["Pinned"].AsBoolean() && !r["Interior"].AsBoolean() &&
                 r["Object"].AsUInt64(Sos.Addr) != 0 && r["Type"].Contains("System.Object"),
            "a normal System.Object root (neither pinned nor interior)");
    }
}

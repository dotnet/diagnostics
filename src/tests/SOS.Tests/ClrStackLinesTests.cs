// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// Coverage for plain <c>!clrstack</c> (base managed stack with source <c>[file @ line]</c>) and
/// <c>-n</c> (suppress source line numbers). The legacy scripts hardcoded method names and exact
/// source file/line (e.g. SimpleThrow.script asserted <c>UserObject.cs @ 19</c>); we do the same kind
/// of sensible hardcoding — assert the expected managed methods appear in caller order and resolve to
/// the expected source file — then use cross-variant self-consistency for <c>-n</c>: it produces the
/// identical frames (same IP / Function per row) but with the source annotation stripped.
///
/// Two harness fidelity fixes make source lines resolve on every config: the dbgeng host now enables
/// SYMOPT_LOAD_LINES (<c>.lines -e</c>), and net48 targets emit a full Windows PDB (see
/// testtargets/Directory.Build.props) — without these, cdb / desktop showed no <c>[file @ line]</c>.
/// </summary>
public sealed class ClrStackLinesTests
{
    private sealed record Frame(string Function, string SourceFile, params int[] SourceLines);

    // Hardcoded like the legacy scripts: distinctive method substrings expected on each target's stack
    // (in caller order) and the source file each resolves to.
    private static IReadOnlyList<Frame> ExpectedFrames(TestConfig config) => config.Target switch
    {
        TargetCatalog.SimpleThrow =>
            [new("UseObject", "UserObject.cs", 19), new("Simple.Main", "SimpleThrow.cs", 12)],
        TargetCatalog.LineNums =>
            [new(".Bar(", "Program.cs"), new(".Foo(", "Program.cs"), new(".Main(", "Program.cs")],
        TargetCatalog.DivZero =>
            [
                new(".DivideByZero(", "DivZero.cs", 15),
                new(".F3(", "DivZero.cs", 24),
                new(".F2(", "DivZero.cs", 36),
                new(".Main(", "DivZero.cs", config.Flavor == Flavor.Framework ? 56 : 57),
            ],
        TargetCatalog.NestedException =>
            [new(".Main(", "NestedExceptionTest.cs", config.Flavor == Flavor.Framework ? [11, 20] : [20])],
        TargetCatalog.Scenarios =>
            [new(".ArgsLocalsMethod(", "SosHarnessScenarios.cs"), new(".Main(", "SosHarnessScenarios.cs")],
        _ => throw new ArgumentOutOfRangeException(nameof(config), config.Target, "no expected frames"),
    };

    public static TheoryData<TestConfig> Matrix { get; }
        = TestMatrices.StackWalk(
            [
                TargetCatalog.SimpleThrow,
                TargetCatalog.LineNums,
                TargetCatalog.DivZero,
                TargetCatalog.NestedException,
                TargetCatalog.Scenarios,
            ],
            liveness: Liveness.AllValid);

    [SosTheory]
    [MemberData(nameof(Matrix))]
    public async Task ClrStack_SourceLines(TestConfig config)
    {
        TestMatrices.SkipUnavailableMacOsDotnetDumpThreads(config);

        using Target target = await Targets.GetTargetAsync(config);
        if (config.Target == TargetCatalog.Scenarios)
        {
            target.GoToStopPoint(TargetCatalog.StopArgsLocals);
        }
        else
        {
            target.GoToFirstStop();
        }

        SosTable plain = target.Clrstack();

        // The expected managed methods appear, in caller order, each resolving to its source file with
        // a real (positive) line number.
        int searchFrom = 0;
        foreach (Frame expected in ExpectedFrames(config))
        {
            int at = IndexOfFrame(plain, expected.Function, searchFrom);
            Assert.True(at >= 0, $"Expected frame '{expected.Function}' at/after row {searchFrom} in:\n{Dump(plain)}");
            SosRow row = plain.Row(at);

            Assert.Equal(expected.SourceFile, Path.GetFileName(row["SourceFile"].Value));
            int lineNumber = row["LineNumber"].AsInt32(Sos.Integer);
            if (expected.SourceLines.Length == 0)
            {
                Assert.True(lineNumber > 0, $"Expected a positive line number for '{expected.Function}'.");
            }
            else
            {
                Assert.Contains(lineNumber, expected.SourceLines);
            }
            searchFrom = at + 1;
        }

        // -n produces the identical frames (same count, same IP and Function per row) but with the
        // source annotation suppressed everywhere.
        SosTable noLines = target.Clrstack(suppressLines: true);
        Assert.Equal(plain.Length, noLines.Length);
        for (int i = 0; i < plain.Length; i++)
        {
            Assert.Equal(plain.Row(i)["IP"].Value, noLines.Row(i)["IP"].Value);
            Assert.Equal(plain.Row(i)["Function"].Value, noLines.Row(i)["Function"].Value);
        }

        Assert.All(noLines, r =>
        {
            Assert.Empty(r["SourceFile"].Value);
            Assert.Empty(r["LineNumber"].Value);
        });

        // Sanity: plain really did carry source info (so the -n strip above is meaningful).
        Assert.Contains(plain, r => r["SourceFile"].Value.Length > 0);
    }

    private static int IndexOfFrame(SosTable table, string functionSubstring, int from)
    {
        for (int i = from; i < table.Length; i++)
        {
            SosRow row = table.Row(i);
            if (!row["InternalFrame"].AsBoolean() && row["Function"].Value.Contains(functionSubstring, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private static string Dump(SosTable table) =>
        string.Join("\n", table.Select(r => $"{r["Child SP"].Value} {r["IP"].Value} {r["Call Site"].Value}"));
}

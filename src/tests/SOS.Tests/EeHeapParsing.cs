// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Text.RegularExpressions;
using SOS.TestHarness;

namespace SOS.Tests;

/// <summary>
/// Runs <c>!eeheap -gc</c> and parses its deeply-nested GC-heap layout into a structured model. Unlike
/// the flat tabular SOS commands, eeheap has a heap-of-heaps shape with several sub-sections and two
/// quite different layouts:
/// <list type="bullet">
///   <item><b>Regions / DATAS</b> (modern .NET Core, workstation or server): a <c>DATAS =</c> banner,
///   <c>Number of GC Heaps: N</c>, then per heap (server prints <c>Heap N (addr)</c> blocks; workstation
///   a single implicit heap) a <c>Small object heap</c> split into <c>generation 0:/1:/2:</c> segment
///   lists, plus <c>NonGC heap</c> / <c>Large object heap</c> / <c>Pinned object heap</c> / (optionally)
///   <c>Frozen object heap</c> segment lists, and a final <c>GC Allocated/Committed Heap Size</c> footer.</item>
///   <item><b>Segment / ephemeral</b> (.NET Framework, older Core): <c>generation N starts at &lt;addr&gt;</c>
///   boundaries + an ephemeral allocation context, a <c>Small object heap</c> segment list (not split by
///   generation), and <c>Large object heap starts at &lt;addr&gt;</c> segments.</item>
/// </list>
/// The model abstracts both behind <see cref="EeHeap.GenerationRanges"/>, the primitive the
/// generation/region tests use to bound an object's address by its generation. The parser is multi-heap
/// first (workstation is the N=1 case) and fails loudly on an unrecognized line rather than dropping it.
/// </summary>
internal static class EeHeapParsing
{
    public static EeHeap EeHeap(this Target target) => new(target.Sos("eeheap -gc"));
}

/// <summary>A GC generation / sub-heap selector for <see cref="EeHeap.GenerationRanges"/>.</summary>
public enum GcGeneration
{
    Gen0,
    Gen1,
    Gen2,
    Loh,
    Poh,
    NonGc,
    Foh,
}

/// <summary>A size as eeheap prints it: <c>0x&lt;hex&gt; (&lt;decimal&gt;)</c>.</summary>
public readonly record struct EeHeapSize(ulong Hex, long Decimal);

/// <summary>One segment/region row: the segment address and its begin/allocated/committed pointers + sizes.</summary>
public sealed record EeHeapSegment(
    ulong Segment, ulong Begin, ulong Allocated, ulong Committed, EeHeapSize? AllocatedSize, EeHeapSize CommittedSize)
{
    /// <summary>The live object range [Begin, Allocated).</summary>
    public (ulong Start, ulong End) Range => (Begin, Allocated);
}

/// <summary>One GC heap (workstation has a single implicit heap; server has one per <c>Heap N</c> block).</summary>
public sealed class EeHeapNode
{
    public int Index { get; init; }

    /// <summary>The heap address from a server <c>Heap N (addr)</c> line, or null for the workstation heap.</summary>
    public ulong? Address { get; init; }

    // Regions layout: the SOH split per generation.
    public List<EeHeapSegment> Gen0 { get; } = new();
    public List<EeHeapSegment> Gen1 { get; } = new();
    public List<EeHeapSegment> Gen2 { get; } = new();

    // Segment layout: the SOH segments (not split per generation) + the generation-start boundaries.
    public List<EeHeapSegment> Soh { get; } = new();
    public ulong? Gen0Start { get; set; }
    public ulong? Gen1Start { get; set; }
    public ulong? Gen2Start { get; set; }

    public List<EeHeapSegment> Loh { get; } = new();
    public List<EeHeapSegment> Poh { get; } = new();
    public List<EeHeapSegment> NonGc { get; } = new();
    public List<EeHeapSegment> Foh { get; } = new();

    /// <summary>The address ranges of <paramref name="generation"/> on this heap, for both layouts.</summary>
    public IReadOnlyList<(ulong Start, ulong End)> Ranges(GcGeneration generation)
    {
        switch (generation)
        {
            case GcGeneration.Loh:
                return Loh.Select(s => s.Range).ToList();
            case GcGeneration.Poh:
                return Poh.Select(s => s.Range).ToList();
            case GcGeneration.NonGc:
                return NonGc.Select(s => s.Range).ToList();
            case GcGeneration.Foh:
                return Foh.Select(s => s.Range).ToList();
        }

        // Generations 0/1/2: regions layout has per-generation segment lists.
        if (Gen0.Count > 0 || Gen1.Count > 0 || Gen2.Count > 0)
        {
            List<EeHeapSegment> segments = generation switch
            {
                GcGeneration.Gen0 => Gen0,
                GcGeneration.Gen1 => Gen1,
                _ => Gen2,
            };
            return segments.Select(s => s.Range).ToList();
        }

        // Segment/ephemeral layout: derive gen ranges from the generation-start boundaries and the SOH
        // segments. gen0/1/2 live in the ephemeral segment (the one that contains gen0Start); older gen2
        // objects live in the remaining SOH segments.
        if (Gen0Start is not ulong g0 || Gen1Start is not ulong g1 || Gen2Start is not ulong g2)
        {
            return Array.Empty<(ulong, ulong)>();
        }

        EeHeapSegment? ephemeral = Soh.FirstOrDefault(s => g0 >= s.Begin && g0 < s.Committed);
        List<(ulong, ulong)> ranges = new();
        switch (generation)
        {
            case GcGeneration.Gen0:
                ranges.Add((g0, ephemeral?.Allocated ?? g0));
                break;
            case GcGeneration.Gen1:
                ranges.Add((g1, g0));
                break;
            default: // Gen2: [gen2Start, gen1Start) in the ephemeral segment + every other SOH segment.
                ranges.Add((g2, g1));
                foreach (EeHeapSegment s in Soh)
                {
                    if (s != ephemeral)
                    {
                        ranges.Add(s.Range);
                    }
                }

                break;
        }

        return ranges;
    }
}

/// <summary>The parsed <c>!eeheap -gc</c> output.</summary>
public sealed class EeHeap
{
    private static readonly Regex s_heapLine = new(@"^Heap\s+(\d+)\s+\(([0-9a-fA-F`]+)\)", RegexOptions.Compiled);
    private static readonly Regex s_genStarts = new(@"^generation\s+(\d)\s+starts at\s+([0-9a-fA-F`]+)", RegexOptions.Compiled);
    private static readonly Regex s_genHeader = new(@"^generation\s+(\d):", RegexOptions.Compiled);
    private static readonly Regex s_lohStartsAt = new(@"^Large object heap starts at\s+([0-9a-fA-F`]+)", RegexOptions.Compiled);
    private static readonly Regex s_size = new(@"Size:\s+0x([0-9a-fA-F]+)\s+\((\d+)\)\s+bytes", RegexOptions.Compiled);

    private readonly SosOutput _output;

    public EeHeap(SosOutput output)
    {
        _output = output;
        Heaps = Parse();
    }

    /// <summary>The raw command output, for custom assertions / failure messages.</summary>
    public SosOutput Output => _output;

    /// <summary>True for the modern regions/DATAS layout; false for the segment/ephemeral layout.</summary>
    public bool IsRegions { get; private set; }

    /// <summary>The DATAS mode value (e.g. "0"/""), or null when there is no DATAS banner (segment layout).</summary>
    public string? Datas { get; private set; }

    /// <summary>The "Number of GC Heaps: N" value.</summary>
    public int HeapCount { get; private set; }

    public IReadOnlyList<EeHeapNode> Heaps { get; }

    public EeHeapSize? GcAllocatedHeapSize { get; private set; }

    public EeHeapSize? GcCommittedHeapSize { get; private set; }

    /// <summary>The address ranges of <paramref name="generation"/> across every heap.</summary>
    public IReadOnlyList<(ulong Start, ulong End)> GenerationRanges(GcGeneration generation) =>
        Heaps.SelectMany(h => h.Ranges(generation)).ToList();

    /// <summary>True if <paramref name="address"/> falls inside one of <paramref name="generation"/>'s ranges.</summary>
    public bool Contains(GcGeneration generation, ulong address) =>
        GenerationRanges(generation).Any(r => address >= r.Start && address < r.End);

    private List<EeHeapNode> Parse()
    {
        List<EeHeapNode> heaps = new();
        EeHeapNode? heap = null;
        List<EeHeapSegment>? target = null; // the list current segment rows go into

        foreach (string raw in _output.Lines)
        {
            string line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("DATAS", StringComparison.Ordinal))
            {
                IsRegions = true;
                int eq = line.IndexOf('=');
                Datas = eq >= 0 ? line[(eq + 1)..].Trim() : string.Empty;
                continue;
            }

            if (line.StartsWith("Number of GC Heaps:", StringComparison.Ordinal))
            {
                HeapCount = int.Parse(line["Number of GC Heaps:".Length..].Trim(), CultureInfo.InvariantCulture);
                continue;
            }

            Match heapMatch = s_heapLine.Match(line);
            if (heapMatch.Success)
            {
                heap = new EeHeapNode { Index = int.Parse(heapMatch.Groups[1].Value, CultureInfo.InvariantCulture), Address = Hex(heapMatch.Groups[2].Value) };
                heaps.Add(heap);
                target = null;
                continue;
            }

            if (line.StartsWith("GC Allocated Heap Size", StringComparison.Ordinal))
            {
                GcAllocatedHeapSize = ParseSize(line);
                continue;
            }

            if (line.StartsWith("GC Committed Heap Size", StringComparison.Ordinal))
            {
                GcCommittedHeapSize = ParseSize(line);
                continue;
            }

            // Separators and the columnar segment header carry no data and must not trigger an implicit
            // heap (the regions banner has a `====` rule before the first `Heap N` block).
            if (line.StartsWith("segment", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal) || line.StartsWith("===", StringComparison.Ordinal))
            {
                continue;
            }

            // Everything below belongs to a heap; create the implicit workstation heap on first need.
            heap ??= NewImplicitHeap(heaps);

            if (line.StartsWith("Small object heap", StringComparison.Ordinal))
            {
                target = heap.Soh; // regions: switched to Gen{N} by the "generation N:" subheaders below
                continue;
            }

            Match genHeader = s_genHeader.Match(line);
            if (genHeader.Success)
            {
                target = genHeader.Groups[1].Value switch
                {
                    "0" => heap.Gen0,
                    "1" => heap.Gen1,
                    _ => heap.Gen2,
                };
                continue;
            }

            Match genStart = s_genStarts.Match(line);
            if (genStart.Success)
            {
                ulong addr = Hex(genStart.Groups[2].Value);
                switch (genStart.Groups[1].Value)
                {
                    case "0": heap.Gen0Start = addr; break;
                    case "1": heap.Gen1Start = addr; break;
                    default: heap.Gen2Start = addr; break;
                }

                continue;
            }

            if (line.StartsWith("ephemeral segment", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("NonGC heap", StringComparison.Ordinal))
            {
                target = heap.NonGc;
                continue;
            }

            Match lohStarts = s_lohStartsAt.Match(line);
            if (lohStarts.Success || line.StartsWith("Large object heap", StringComparison.Ordinal))
            {
                target = heap.Loh;
                continue;
            }

            if (line.StartsWith("Pinned object heap", StringComparison.Ordinal))
            {
                target = heap.Poh;
                continue;
            }

            if (line.StartsWith("Frozen object heap", StringComparison.Ordinal))
            {
                target = heap.Foh;
                continue;
            }

            // A segment data row (only valid inside a section).
            if (TryParseSegment(line, out EeHeapSegment? segment))
            {
                if (target is null)
                {
                    throw _output.Fail($"a segment row outside any heap section: '{raw}'");
                }

                target.Add(segment!);
                continue;
            }

            // Anything else is unexpected — fail loudly rather than silently dropping eeheap data.
            throw _output.Fail($"a recognized eeheap line, but got: '{raw}'");
        }

        return heaps;
    }

    private static EeHeapNode NewImplicitHeap(List<EeHeapNode> heaps)
    {
        EeHeapNode heap = new() { Index = 0 };
        heaps.Add(heap);
        return heap;
    }

    private EeHeapSize ParseSize(string line)
    {
        Match m = s_size.Match(line);
        if (!m.Success)
        {
            throw _output.Fail($"a 'Size: 0x.. (NNN) bytes' value in: '{line}'");
        }

        return new EeHeapSize(
            ulong.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    private static bool TryParseSegment(string line, out EeHeapSegment? segment)
    {
        segment = null;
        string[] t = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        // <segment> <begin> <allocated> <committed> [<allocHex> (<allocDec>)] <commitHex> (<commitDec>)
        // => 8 tokens with an allocated size, 6 without (an empty region prints no allocated size).
        if ((t.Length != 8 && t.Length != 6) || !IsHex(t[0]) || !IsHex(t[1]) || !IsHex(t[2]) || !IsHex(t[3]))
        {
            return false;
        }

        EeHeapSize? allocSize = null;
        EeHeapSize commitSize;
        if (t.Length == 8)
        {
            if (!TryHexParen(t[4], t[5], out EeHeapSize a) || !TryHexParen(t[6], t[7], out EeHeapSize c))
            {
                return false;
            }

            allocSize = a;
            commitSize = c;
        }
        else
        {
            if (!TryHexParen(t[4], t[5], out EeHeapSize c))
            {
                return false;
            }

            commitSize = c;
        }

        segment = new EeHeapSegment(Hex(t[0]), Hex(t[1]), Hex(t[2]), Hex(t[3]), allocSize, commitSize);
        return true;
    }

    // A "0x<hex>" + "(<dec>)" pair.
    private static bool TryHexParen(string hex, string paren, out EeHeapSize size)
    {
        size = default;
        string h = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
        if (!paren.StartsWith('(') || !paren.EndsWith(')'))
        {
            return false;
        }

        string d = paren[1..^1];
        if (!ulong.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong hv) ||
            !long.TryParse(d, NumberStyles.None, CultureInfo.InvariantCulture, out long dv))
        {
            return false;
        }

        size = new EeHeapSize(hv, dv);
        return true;
    }

    private static ulong Hex(string token) =>
        ulong.Parse(token.Replace("`", string.Empty), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static bool IsHex(string token)
    {
        string s = token.Replace("`", string.Empty);
        return s.Length > 0 && s.All(Uri.IsHexDigit);
    }
}

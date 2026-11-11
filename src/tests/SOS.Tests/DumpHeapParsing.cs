// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Text.RegularExpressions;
using SOS.TestHarness;

namespace SOS.Tests;

/// <summary>
/// Runs <c>!dumpheap</c> and parses its several output shapes (see SOS's <c>DumpHeapService</c>):
/// the object listing (<c>Address MT Size</c> + an unheadered trailing <c>Free</c>), the statistics
/// table (<c>MT Count TotalSize Class Name</c>), the <c>-strings</c> headerless summary
/// (<c>Count TotalSize String</c>), the <c>-short</c> bare-address list, and the
/// <c>Total N objects, M bytes</c> footer. Sections are parsed lazily and absent ones throw a rich
/// failure (so a test that asks for the wrong section for its flags gets the full output).
/// </summary>
internal static class DumpHeapParsing
{
    /// <summary>Run <c>!dumpheap &lt;args&gt;</c> once and wrap the output for section-by-section parsing.</summary>
    public static DumpHeapResult DumpHeap(this Target target, string args = "")
    {
        string command = string.IsNullOrWhiteSpace(args) ? "dumpheap" : $"dumpheap {args}";
        return new DumpHeapResult(target.Sos(command));
    }
}

internal sealed class DumpHeapResult
{
    private static readonly Regex s_totalRegex =
        new(@"Total\s+([\d,]+)\s+objects,\s+([\d,]+)\s+bytes", RegexOptions.Compiled);

    private readonly SosOutput _output;
    private SosTable? _objects;
    private SosTable? _statistics;
    private SosTable? _strings;
    private SosTable? _thinLocks;
    private IReadOnlyList<ulong>? _short;

    public DumpHeapResult(SosOutput output) => _output = output;

    /// <summary>The raw command output (host/command/full text), for custom assertions.</summary>
    public SosOutput Output => _output;

    /// <summary>The object listing: <c>Address</c>, <c>MT</c>, <c>Size</c>, and <c>Free</c> ("Free" or "").</summary>
    public SosTable Objects => _objects ??= ParseObjects();

    /// <summary>The statistics table: <c>MT</c>, <c>Count</c>, <c>TotalSize</c>, <c>Class Name</c>.</summary>
    public SosTable Statistics => _statistics ??= _output.Table(
        ColumnAlignment.Right("MT"), ColumnAlignment.Right("Count"), ColumnAlignment.Right("TotalSize"), "Class Name");

    /// <summary>The <c>-strings</c> value summary (headerless in SOS): <c>Count</c>, <c>TotalSize</c>, <c>String</c>.</summary>
    public SosTable Strings => _strings ??= ParseStrings();

    /// <summary>The <c>-short</c> bare-address list.</summary>
    public IReadOnlyList<ulong> ShortAddresses => _short ??= ParseShort();

    /// <summary>True if a statistics table is present (it is omitted entirely when no objects match).</summary>
    public bool HasStatistics => FindStatisticsHeader() >= 0;

    /// <summary>
    /// The <c>Count</c> the statistics table reports for the type whose Class Name equals
    /// <paramref name="className"/>, or 0 if the type is absent (or no statistics were printed because
    /// nothing matched). Handy for live/dead filtering where "zero" prints no table at all.
    /// </summary>
    public int CountOf(string className)
    {
        if (!HasStatistics)
        {
            return 0;
        }

        foreach (SosRow row in Statistics)
        {
            if (row["Class Name"].Value == className)
            {
                return row["Count"].AsInt32(Sos.Integer);
            }
        }

        return 0;
    }

    /// <summary>The footer's total object count.</summary>
    public long TotalObjects => ParseTotals().Objects;

    /// <summary>The footer's total byte count.</summary>
    public long TotalBytes => ParseTotals().Bytes;

    /// <summary>
    /// The <c>-thinlock</c> table: <c>Object</c>, <c>Thread</c>, <c>OSId</c>, <c>Recursion</c> (one row per
    /// object that carries a thin lock). Empty (a throwing parse) when no header is present.
    /// </summary>
    public SosTable ThinLocks => _thinLocks ??= _output.Table(
        ColumnAlignment.Right("Object"), ColumnAlignment.Right("Thread"),
        ColumnAlignment.Right("OSId"), ColumnAlignment.Right("Recursion"));

    /// <summary>True if a <c>-thinlock</c> table header is present (absent when no thin locks were found).</summary>
    public bool HasThinLocks => FindHeader("Object", "Thread", "OSId", "Recursion") >= 0;

    private SosTable ParseObjects()
    {
        IReadOnlyList<string> lines = _output.Lines;
        int header = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            if (line.Contains("Address", StringComparison.Ordinal) &&
                line.Contains("MT", StringComparison.Ordinal) &&
                line.Contains("Size", StringComparison.Ordinal))
            {
                header = i;
                break;
            }
        }

        if (header < 0)
        {
            throw _output.Fail("a dumpheap object listing (an 'Address MT Size' header)");
        }

        List<string[]> rows = new();
        for (int i = header + 1; i < lines.Count; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line) ||
                line.StartsWith("Statistics:", StringComparison.Ordinal) ||
                line.TrimStart().StartsWith("Total ", StringComparison.Ordinal))
            {
                break;
            }

            string[] tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            // A data row is "<addr> <mt> <size> [Free]"; stop at the first non-address line.
            if (tokens.Length < 3 || !IsHex(tokens[0]))
            {
                break;
            }

            string free = tokens.Length > 3 ? string.Join(' ', tokens[3..]) : string.Empty;
            rows.Add([tokens[0], tokens[1], tokens[2], free]);
        }

        return _output.TableFromRows(["Address", "MT", "Size", "Free"], rows);
    }

    private SosTable ParseStrings()
    {
        IReadOnlyList<string> lines = _output.Lines;
        int statsLine = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith("Statistics:", StringComparison.Ordinal))
            {
                statsLine = i;
                break;
            }
        }

        if (statsLine < 0)
        {
            throw _output.Fail("a dumpheap -strings summary (a 'Statistics:' section)");
        }

        List<string[]> rows = new();
        for (int i = statsLine + 1; i < lines.Count; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            // "<count> <totalsize> <string...>"; the string may be empty or contain spaces.
            if (tokens.Length < 2 || !IsDecimal(tokens[0]) || !IsDecimal(tokens[1]))
            {
                break;
            }

            string value = tokens.Length > 2 ? string.Join(' ', tokens[2..]) : string.Empty;
            rows.Add([tokens[0], tokens[1], value]);
        }

        return _output.TableFromRows(["Count", "TotalSize", "String"], rows);
    }

    private IReadOnlyList<ulong> ParseShort()
    {
        List<ulong> addresses = new();
        foreach (string line in _output.Lines)
        {
            string t = line.Trim();
            if (t.Length == 0)
            {
                continue;
            }

            // -short prints only bare addresses; tolerate a stray warning line by skipping non-hex.
            if (IsHex(t) && ulong.TryParse(t.Replace("`", string.Empty), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong addr))
            {
                addresses.Add(addr);
            }
        }

        return addresses;
    }

    private (long Objects, long Bytes) ParseTotals()
    {
        Match m = s_totalRegex.Match(_output.Text);
        if (!m.Success)
        {
            throw _output.Fail("a dumpheap 'Total N objects, M bytes' footer");
        }

        long objects = long.Parse(m.Groups[1].Value.Replace(",", string.Empty), CultureInfo.InvariantCulture);
        long bytes = long.Parse(m.Groups[2].Value.Replace(",", string.Empty), CultureInfo.InvariantCulture);
        return (objects, bytes);
    }

    private int FindStatisticsHeader()
    {
        IReadOnlyList<string> lines = _output.Lines;
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            if (line.Contains("MT", StringComparison.Ordinal) &&
                line.Contains("Count", StringComparison.Ordinal) &&
                line.Contains("TotalSize", StringComparison.Ordinal) &&
                line.Contains("Class Name", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Index of the first line containing every one of <paramref name="columns"/>, or -1.</summary>
    private int FindHeader(params string[] columns)
    {
        IReadOnlyList<string> lines = _output.Lines;
        for (int i = 0; i < lines.Count; i++)
        {
            if (columns.All(c => lines[i].Contains(c, StringComparison.Ordinal)))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsHex(string token)
    {
        string s = token.Replace("`", string.Empty);
        if (s.Length == 0)
        {
            return false;
        }

        foreach (char c in s)
        {
            if (!Uri.IsHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsDecimal(string token)
    {
        string s = token.Replace(",", string.Empty);
        if (s.Length == 0)
        {
            return false;
        }

        foreach (char c in s)
        {
            if (!char.IsDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}

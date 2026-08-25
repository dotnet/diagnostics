// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.TestHarness;

namespace SOS.Tests;

/// <summary>
/// Parser for the tabular section of <c>!clrthreads</c>. The generic <see cref="SosOutput.Table"/>
/// can't read it: SOS prints the header across two physical lines (the "Lock" of the "Lock Count"
/// column floats on the line above "Count" — see strike.cpp's
/// <c>WriteColumn(8, "Lock")</c> before the <c>WriteRow(...)</c>), and the trailing <c>Exception</c>
/// column is empty for most threads, which would make the fixed-width row detector reject every row.
///
/// Every column up to <c>Apt</c> is a single whitespace token (the <c>GC Alloc Context</c> is
/// <c>addr:addr</c> with no space), so we tokenize each data line: the first N tokens are the fixed
/// columns and everything after is the free-form <c>Exception</c> column (thread tags like
/// <c>(Finalizer)</c>/<c>(GC)</c>/<c>(Threadpool Worker)</c> and any last-thrown exception).
/// </summary>
internal static class ClrThreadsParsing
{
    // Columns in SOS print order (strike.cpp PrintThreadsFromThreadStore). "Lock Count" rejoins the
    // two-line header; "Exception" is the trailing free-form column.
    private static readonly string[] s_columns =
        ["DBG", "ID", "OSID", "ThreadOBJ", "State", "GC Mode", "GC Alloc Context", "Domain", "Lock Count", "Apt", "Exception"];

    // When the runtime is hosted, SOS inserts a "Fiber" column before "Exception".
    private static readonly string[] s_hostedColumns =
        ["DBG", "ID", "OSID", "ThreadOBJ", "State", "GC Mode", "GC Alloc Context", "Domain", "Lock Count", "Apt", "Fiber", "Exception"];

    /// <summary>
    /// Parse the <c>!clrthreads</c> thread table into a <see cref="SosTable"/>. Rows are addressable by
    /// column name (<c>row["GC Mode"]</c>, <c>row["Lock Count"]</c>, <c>row["Exception"]</c>); cells
    /// convert implicitly to their string value, so <c>row["GC Mode"] == "Preemptive"</c> works.
    /// </summary>
    public static SosTable AsThreadsTable(this SosOutput output)
    {
        string[] columns = IsHosted(output) ? s_hostedColumns : s_columns;
        int fixedCount = columns.Length - 1; // everything before the trailing Exception column

        IReadOnlyList<string> lines = output.Lines;
        int header = FindHeaderLine(lines);
        if (header < 0)
        {
            throw output.Fail("a !clrthreads thread table (a header row with DBG / ThreadOBJ / Apt)");
        }

        List<string[]> rows = new();
        for (int i = header + 1; i < lines.Count; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                break; // blank line terminates the table
            }

            string[] tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < fixedCount)
            {
                break; // not a data row — end of the table
            }

            string[] cells = new string[columns.Length];
            for (int c = 0; c < fixedCount; c++)
            {
                cells[c] = tokens[c];
            }

            // The remaining tokens (if any) are the free-form Exception column; empty when absent.
            cells[fixedCount] = tokens.Length > fixedCount
                ? string.Join(' ', tokens[fixedCount..])
                : string.Empty;

            rows.Add(cells);
        }

        return output.TableFromRows(columns, rows);
    }

    private static bool IsHosted(SosOutput output)
    {
        foreach (string line in output.Lines)
        {
            string t = line.Trim();
            if (t.StartsWith("Hosted Runtime:", StringComparison.Ordinal))
            {
                return t["Hosted Runtime:".Length..].Trim() != "no";
            }
        }

        return false;
    }

    private static int FindHeaderLine(IReadOnlyList<string> lines)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            string t = lines[i].TrimStart();
            if (t.StartsWith("DBG", StringComparison.Ordinal) &&
                lines[i].Contains("ThreadOBJ", StringComparison.Ordinal) &&
                lines[i].Contains("Apt", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}

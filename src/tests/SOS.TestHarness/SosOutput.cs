// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.RegularExpressions;

namespace SOS.TestHarness;

/// <summary>
/// The captured output of a single SOS/debugger command, plus a fluent, chainable
/// assertion surface. This is the modern replacement for a wall of <c>VERIFY:</c>
/// regex lines.
///
/// Three assertion styles are offered side by side, on purpose:
/// <list type="bullet">
///   <item><b>Field lookup + value assertions</b> — <c>.Field(name)</c> returns a structured
///   <see cref="SosField"/> (Key/Value). Assert shape inline with a token
///   (<c>.Field("Exception object", Sos.Addr)</c>) and/or an exact value on the returned field
///   (<c>.Field("Exception type").Matches("System.InvalidOperationException")</c>,
///   <c>.Field("Exception object").Matches(0x1234, Sos.Addr)</c>,
///   <c>.Field("InnerException").MatchesRegex(@"System\.FormatException")</c>). One assertion per
///   statement keeps each on its own stack-trace line.</item>
///   <item><b>Structural line assertions</b> — <c>.Frame("Program.Main")</c>,
///   <c>.ContainsLineMatching(...)</c>.</item>
///   <item><b>Raw regex escape hatch</b> — <c>.Matches(@"HResult:\s+80131509")</c> /
///   <c>.DoesNotMatch(...)</c>, for the cases where a pattern really is the clearest thing.</item>
/// </list>
/// All assertions chain (return <c>this</c>) and throw <see cref="SosAssertException"/> with the
/// full captured output on failure.
/// </summary>
public sealed class SosOutput
{
    public string Host { get; }
    public string Command { get; }
    public string Text { get; }

    private readonly string[] _lines;

    public SosOutput(string host, string command, string text)
    {
        Host = host;
        Command = command;
        Text = text ?? string.Empty;
        _lines = Text.Replace("\r\n", "\n").Split('\n');
    }

    public IReadOnlyList<string> Lines => _lines;

    public override string ToString() => Text;

    // ---- Field lookup --------------------------------------------------------------------

    /// <summary>
    /// Look up a "Name: value" line and return it as a structured <see cref="SosField"/>. The field
    /// carries the value assertions (<see cref="SosField.AssertValid"/>, <see cref="SosField.AssertNotEmpty"/>,
    /// numeric parsing) and converts implicitly to its value string for <c>Assert.Equal</c>. Throws
    /// if the field is absent. The <see cref="this[string]"/> indexer is shorthand for this.
    /// </summary>
    public SosField Field(string name) => new(this, name, RequireField(name));

    /// <summary>Shorthand for <see cref="Field(string)"/>: <c>pe["Message"]</c>.</summary>
    public SosField this[string name] => Field(name);

    private string RequireField(string name) =>
        FindFieldValue(name) ?? throw Fail($"a field named '{name}'");

    // ---- Tables --------------------------------------------------------------------------

    /// <summary>
    /// Parse the columnar block whose header row is <paramref name="columns"/> (e.g. the
    /// <c>printexception</c> StackTrace table's <c>SP</c>/<c>IP</c>/<c>Function</c>), asserting the
    /// header row is present. Cells are sliced at fixed header start columns (tables are aligned), so
    /// space-containing values (signatures) survive in any column. A column written as
    /// <c>(name, token)</c> has every cell validated against that token up front; a bare name is not
    /// validated here (use <see cref="SosTable.AssertValid"/> later if you want). Columns may be
    /// freely mixed — see <see cref="SosColumn"/>.
    /// </summary>
    public SosTable Table(params SosColumn[] columns) => BuildTable(columns, null);

    /// <summary>
    /// Parse a table whose aligned rows are interleaved with <em>internal data</em> lines that belong
    /// to the row above them — e.g. <c>!clrstack -gc</c> (GC roots under each frame) or
    /// <c>!clrstack -r</c> (a register dump under each frame). After the first aligned row is matched,
    /// each subsequent line is offered to <paramref name="data"/> first: if it returns <c>true</c> the
    /// line is consumed and any cells it added to the most recent row (via
    /// <see cref="SosRow.AddData(SosCell)"/>) show up in <see cref="SosRow.Data"/>; if it returns
    /// <c>false</c> the line falls through to normal row matching (a new row, or the end of the table).
    /// Data is never collected before the first row.
    /// </summary>
    public SosTable Table(SosColumn[] columns, SosDataExtractor dataExtractor) => BuildTable(columns, dataExtractor);

    /// <summary>
    /// Build a table from cells a caller already parsed, for commands whose layout the generic
    /// <see cref="Table"/> parser can't handle — e.g. <c>!clrthreads</c>, whose header spans two
    /// physical lines (the "Lock" of "Lock Count" floats above "Count") and whose trailing column is
    /// usually empty. The harness still owns <see cref="SosTable"/>/<see cref="SosRow"/> construction
    /// (so the rows carry this output for rich failures); the caller supplies the column names and one
    /// <c>string[]</c> of cells per row (each the length of <paramref name="columns"/>).
    /// </summary>
    public SosTable TableFromRows(string[] columns, IEnumerable<string[]> rows) =>
        new(this, columns, rows as string[][] ?? rows.ToArray());

    private SosTable BuildTable(SosColumn[] columns, SosDataExtractor? data)
    {
        string[] headers = columns.Select(c => c.Header).ToArray();
        bool[] rightAligned = columns.Select(c => c.RightAligned).ToArray();
        SosTable table = ParseTable(headers, rightAligned, data);

        for (int c = 0; c < columns.Length; c++)
        {
            SosToken? token = columns[c].Token;
            if (token is null)
            {
                continue;
            }

            for (int r = 0; r < table.Length; r++)
            {
                string cell = table.Row(r)[c];
                if (!token.Matches(cell))
                {
                    throw Fail($"every '{headers[c]}' cell to be a {token} value (row {r} was \"{cell}\")");
                }
            }
        }

        return table;
    }

    private SosTable ParseTable(string[] headers, bool[] rightAligned, SosDataExtractor? data)
    {
        int headerLine = FindHeaderLine(headers, out int[] starts, out int[] ends);
        if (headerLine < 0)
        {
            throw Fail($"a table with header row [{string.Join(", ", headers)}]");
        }

        // Fixed left boundaries of each column, derived from header positions + alignment. A column
        // begins at: its own header start if it's left-aligned, else the previous column's header
        // end (a right-aligned column's data right-aligns under its header, extending left of it).
        int[] bounds = new int[headers.Length];
        bounds[0] = 0;
        for (int c = 1; c < headers.Length; c++)
        {
            bounds[c] = rightAligned[c] ? ends[c - 1] : starts[c];
        }

        List<SosRow> rows = new();
        for (int i = headerLine + 1; i < _lines.Length; i++)
        {
            string line = _lines[i];

            // Internal data: once we have a row to attach to, let the extractor claim the line. If it
            // does, the cells it added belong to that row, and we don't try to match a new row.
            if (data is not null && rows.Count > 0 && data(line, rows[^1]))
            {
                continue;
            }

            // The table ends at the first line that isn't an aligned data row (a blank line, or a
            // differently-shaped trailing line). A row must line up with EVERY column's anchor.
            if (!LooksLikeRow(line, starts, ends, rightAligned))
            {
                break;
            }

            rows.Add(new SosRow(this, headers, SliceColumns(line, bounds)));
        }

        return new SosTable(this, headers, rows.ToArray());
    }

    /// <summary>
    /// Find the header row and the character start/end index of each header. SOS tables are
    /// fixed-width/aligned; combined with each column's alignment, those positions let cells be
    /// sliced so space-containing values (signatures, type names) stay intact in any column.
    /// </summary>
    private int FindHeaderLine(string[] headers, out int[] starts, out int[] ends)
    {
        starts = Array.Empty<int>();
        ends = Array.Empty<int>();
        for (int i = 0; i < _lines.Length; i++)
        {
            if (TryLocateHeaders(_lines[i], headers, out int[] s, out int[] e))
            {
                starts = s;
                ends = e;
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Locate each header as an in-order substring of <paramref name="line"/>, each found at or after
    /// the previous header's end. Substring matching (rather than whitespace tokenizing) lets headers
    /// contain spaces — e.g. <c>!clrstack</c>'s "Child SP" and "Call Site" — while a header must sit on
    /// a whitespace/edge boundary so a short header like "IP" can't match inside a longer word.
    /// </summary>
    private static bool TryLocateHeaders(string line, string[] headers, out int[] starts, out int[] ends)
    {
        starts = new int[headers.Length];
        ends = new int[headers.Length];
        int from = 0;
        for (int k = 0; k < headers.Length; k++)
        {
            int at = from;
            while (true)
            {
                at = line.IndexOf(headers[k], at, StringComparison.Ordinal);
                if (at < 0)
                {
                    return false;
                }

                bool leftEdge = at == 0 || char.IsWhiteSpace(line[at - 1]);
                int end = at + headers[k].Length;
                bool rightEdge = end >= line.Length || char.IsWhiteSpace(line[end]);
                if (leftEdge && rightEdge)
                {
                    starts[k] = at;
                    ends[k] = end;
                    from = end;
                    break;
                }

                at = end;
            }
        }

        return true;
    }

    private static string[] SliceColumns(string line, int[] bounds)
    {
        string[] cells = new string[bounds.Length];
        for (int c = 0; c < bounds.Length; c++)
        {
            int begin = Math.Min(bounds[c], line.Length);
            int end = c + 1 < bounds.Length ? Math.Min(bounds[c + 1], line.Length) : line.Length;
            cells[c] = line[begin..Math.Max(begin, end)].Trim();
        }

        return cells;
    }

    /// <summary>
    /// A line is a data row only if it lines up with the column layout at every column's alignment
    /// anchor: a left-aligned column has a content character at its header start (and whitespace
    /// before it); a right-aligned column has a content character at its header's right edge (and
    /// whitespace after it). Checking every column — not just the first — keeps a differently-shaped
    /// trailing line (e.g. "StackTraceString: &lt;none&gt;") from being mistaken for a row.
    /// </summary>
    private static bool LooksLikeRow(string line, int[] starts, int[] ends, bool[] rightAligned)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        // A left-aligned first column implies a blank left margin (preserves prior behavior).
        if (!rightAligned[0])
        {
            for (int i = 0; i < starts[0]; i++)
            {
                if (i >= line.Length || !char.IsWhiteSpace(line[i]))
                {
                    return false;
                }
            }
        }

        for (int c = 0; c < starts.Length; c++)
        {
            if (rightAligned[c])
            {
                int anchor = ends[c] - 1; // data right edge aligns with the header's last char
                if (anchor < 0 || anchor >= line.Length || char.IsWhiteSpace(line[anchor]))
                {
                    return false;
                }

                if (ends[c] < line.Length && !char.IsWhiteSpace(line[ends[c]]))
                {
                    return false;
                }
            }
            else
            {
                int anchor = starts[c]; // data left edge aligns with the header start
                if (anchor >= line.Length || char.IsWhiteSpace(line[anchor]))
                {
                    return false;
                }

                if (anchor > 0 && !char.IsWhiteSpace(line[anchor - 1]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    // ---- Structural line assertions ------------------------------------------------------


    /// <summary>True if the output contains the given literal substring (compose with <c>Assert.True</c>).</summary>
    public bool Contains(string literal) => Text.Contains(literal, StringComparison.Ordinal);

    /// <summary>
    /// Assert the output contains <paramref name="literal"/>. Prefer this over
    /// <c>Assert.True(output.Contains(...))</c>: on failure it throws <see cref="SosAssertException"/>
    /// with the host, command, and full captured output, instead of a bare "Expected: True".
    /// </summary>
    public SosOutput AssertContains(string literal)
    {
        if (!Text.Contains(literal, StringComparison.Ordinal))
        {
            throw Fail($"output to contain \"{literal}\"");
        }

        return this;
    }


    // ---- Column-oriented helpers ---------------------------------------------------------

    /// <summary>
    /// Return the whitespace-split tokens of the first data line whose first token is a hex value
    /// equal to <paramref name="address"/> (tolerant of dbgeng's backtick split). Useful for
    /// column-oriented commands like <c>gcwhere</c>/<c>dumpheap</c>. Throws if no such row exists.
    /// </summary>
    public string[] RowByAddress(ulong address)
    {
        foreach (string line in _lines)
        {
            string[] tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 0 && TryParseHex(tokens[0], out ulong value) && value == address)
            {
                return tokens;
            }
        }

        throw Fail($"a row whose first column is address 0x{address:x}");
    }

    /// <summary>
    /// Header-relative column access for column-oriented output. Finds the header row (the line
    /// containing one of <paramref name="headerAliases"/> as a token), locates that column's
    /// index, then returns the value at that index from the data row whose first column is
    /// <paramref name="address"/>. This tolerates host/flavor layout differences — e.g. SOS prints
    /// <c>gcwhere</c>'s generation column as "Generation" (col 3) under dotnet-dump but as "Gen"
    /// (col 1) under cdb on desktop — without the test hard-coding a column index.
    /// </summary>
    public string Column(ulong address, params string[] headerAliases)
    {
        int headerIndex = -1;
        foreach (string line in _lines)
        {
            string[] tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            foreach (string alias in headerAliases)
            {
                int idx = Array.FindIndex(tokens, tok => string.Equals(tok, alias, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                {
                    headerIndex = idx;
                    break;
                }
            }

            if (headerIndex >= 0)
            {
                break;
            }
        }

        if (headerIndex < 0)
        {
            throw Fail($"a header column named one of [{string.Join(", ", headerAliases)}]");
        }

        string[] row = RowByAddress(address);
        if (headerIndex >= row.Length)
        {
            throw Fail($"column {headerIndex} ([{string.Join(", ", headerAliases)}]) in row [{string.Join(' ', row)}]");
        }

        return row[headerIndex];
    }

    private static bool TryParseHex(string token, out ulong value) =>
        ulong.TryParse(token.Replace("`", string.Empty), System.Globalization.NumberStyles.HexNumber, null, out value);

    // ---- internals -----------------------------------------------------------------------

    private string? FindFieldValue(string name)
    {
        foreach (string line in _lines)
        {
            int colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            string key = line[..colon].Trim();
            if (string.Equals(key, name, StringComparison.Ordinal))
            {
                return line[(colon + 1)..].Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// Build (don't throw) a <see cref="SosAssertException"/> for <paramref name="expectation"/>,
    /// carrying this output's host, command, and full text. The primitive every fluent assertion and
    /// custom command parser uses to produce a rich failure: <c>throw output.Fail("a thread table")</c>.
    /// </summary>
    public SosAssertException Fail(string expectation) => new(Host, Command, expectation, Text);
}

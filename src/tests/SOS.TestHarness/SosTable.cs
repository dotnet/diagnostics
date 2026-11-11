// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;

namespace SOS.TestHarness;

/// <summary>
/// A parsed columnar block of SOS output — e.g. the <c>StackTrace (generated)</c> table in
/// <c>printexception</c> with its <c>SP</c>/<c>IP</c>/<c>Function</c> columns. Produced by
/// <see cref="SosOutput.Table"/>. Columns are identified by header name and cells are
/// sliced at fixed header start columns (SOS tables are aligned), so values that contain spaces (a
/// function signature like <c>Method(System.String, int)+0x8a</c>) stay intact in any column.
///
/// The table is itself an <see cref="IEnumerable{T}"/> of <see cref="SosRow"/>, so LINQ reads
/// naturally: <c>table.Any(row =&gt; row["Function"].Contains("ThrowNested"))</c>.
/// </summary>
public sealed class SosTable : IEnumerable<SosRow>
{
    private readonly SosOutput _output;
    private readonly string[] _columns;
    private readonly SosRow[] _rows;

    internal SosTable(SosOutput output, string[] columns, string[][] rows)
    {
        _output = output;
        _columns = columns;
        _rows = Array.ConvertAll(rows, r => new SosRow(output, columns, r));
    }

    internal SosTable(SosOutput output, string[] columns, SosRow[] rows)
    {
        _output = output;
        _columns = columns;
        _rows = rows;
    }

    /// <summary>The header names, in order.</summary>
    public IReadOnlyList<string> Columns => _columns;

    /// <summary>The number of data rows.</summary>
    public int Length => _rows.Length;

    /// <summary>One data row, by index.</summary>
    public SosRow Row(int index) => _rows[index];

    /// <summary>
    /// Assert that every cell of each column matches the column's token, by position — one token per
    /// column, in column order. This is the post-hoc form of the per-column validation
    /// <see cref="SosOutput.Table"/> does for <c>(name, token)</c> columns, for when the table was
    /// parsed with bare header names: <c>table.AssertValid(Sos.Addr, Sos.Addr, Sos.ModuleFunctionWithOffset)</c>.
    /// </summary>
    public SosTable AssertValid(params SosToken[] columnTokens)
    {
        if (columnTokens.Length != _columns.Length)
        {
            throw _output.Fail($"AssertValid to be given {_columns.Length} token(s) for columns [{string.Join(", ", _columns)}], got {columnTokens.Length}");
        }

        for (int c = 0; c < columnTokens.Length; c++)
        {
            for (int r = 0; r < _rows.Length; r++)
            {
                string cell = _rows[r][c];
                if (!columnTokens[c].Matches(cell))
                {
                    throw _output.Fail($"every '{_columns[c]}' cell to be a {columnTokens[c]} value (row {r} was \"{cell}\")");
                }
            }
        }

        return this;
    }

    /// <summary>All cell values in the named column, top to bottom.</summary>
    public IEnumerable<string> Column(string header)
    {
        foreach (SosRow row in _rows)
        {
            yield return row[header].Value;
        }
    }

    /// <summary>
    /// Assert at least one row satisfies <paramref name="predicate"/>. Prefer this over
    /// <c>Assert.Contains(table, predicate)</c>: on failure it throws <see cref="SosAssertException"/>
    /// with the full captured output (a bare predicate <c>Assert.Contains</c> can't say what it wanted
    /// or echo the SOS output). <paramref name="description"/> names what was sought, e.g.
    /// <c>"Function contains NestedExceptions.ThrowNested"</c>.
    /// </summary>
    public SosTable AssertContainsRow(Func<SosRow, bool> predicate, string description)
    {
        if (!_rows.Any(predicate))
        {
            throw _output.Fail($"a row where {description}");
        }

        return this;
    }

    /// <summary>
    /// Assert every row satisfies <paramref name="predicate"/>. Prefer this over
    /// <c>Assert.All(table, row =&gt; Assert.True(...))</c> when the check is a simple predicate:
    /// <c>table.AssertAll(row =&gt; row["GC Mode"] == "Preemptive", "every thread is Preemptive")</c>.
    /// On failure it throws <see cref="SosAssertException"/> naming the first offending row and echoing
    /// the full captured output.
    /// </summary>
    public SosTable AssertAll(Func<SosRow, bool> predicate, string description)
    {
        for (int r = 0; r < _rows.Length; r++)
        {
            if (!predicate(_rows[r]))
            {
                throw _output.Fail($"every row to satisfy: {description} (row {r} did not: [{string.Join(" | ", _rows[r].Cells)}])");
            }
        }

        return this;
    }

    /// <summary>
    /// Assert exactly one row satisfies <paramref name="predicate"/> and return it. Prefer this over
    /// LINQ <c>table.Single(predicate)</c>: on failure (zero or many matches) it throws
    /// <see cref="SosAssertException"/> with the full captured output.
    /// </summary>
    public SosRow SingleRow(Func<SosRow, bool> predicate, string description)
    {
        SosRow[] matches = _rows.Where(predicate).ToArray();
        if (matches.Length != 1)
        {
            throw _output.Fail($"exactly one row where {description} (found {matches.Length})");
        }

        return matches[0];
    }

    public IEnumerator<SosRow> GetEnumerator() => ((IEnumerable<SosRow>)_rows).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _rows.GetEnumerator();
}

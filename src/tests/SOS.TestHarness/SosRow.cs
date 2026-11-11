// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>
/// One data row of a <see cref="SosTable"/>. Cells are addressable by column name
/// (<c>row["Function"]</c>) or by index, so tests can read like the table they assert against:
/// <c>table.Any(row =&gt; row["Function"].Contains("ThrowNested"))</c>.
///
/// A table data extractor can attach two distinct shapes of internal-data to a row:
/// <list type="bullet">
///   <item><b>Extra scalar columns</b> via <see cref="AddColumn"/> — one value per name, promoted
///   onto the row so <c>row["rip"]</c> works alongside <c>row["IP"]</c> (the <c>!clrstack -r</c>
///   registers).</item>
///   <item><b>Structured sub-records</b> via <see cref="AddData"/> — a list of multi-field
///   <see cref="SosDataRow"/>s in <see cref="Data"/> (the <c>!clrstack -gc</c> roots, or
///   <c>-a</c>/<c>-l</c>/<c>-p</c> parameters/locals).</item>
/// </list>
/// </summary>
public sealed class SosRow
{
    private readonly SosOutput _output;
    private readonly string[] _columns;
    private readonly string[] _cells;
    private readonly List<SosCell> _addedColumns = new();
    private readonly List<SosDataRow> _data = new();

    internal SosRow(SosOutput output, string[] columns, string[] cells)
    {
        _output = output;
        _columns = columns;
        _cells = cells;
    }

    /// <summary>The cell in the named column (an original table column or one added via <see cref="AddColumn"/>).</summary>
    public SosCell this[string column]
    {
        get
        {
            int i = Array.IndexOf(_columns, column);
            if (i >= 0)
            {
                return new SosCell(_output, column, _cells[i]);
            }

            SosCell? added = _addedColumns.FirstOrDefault(c => c.Column == column);
            if (added is not null)
            {
                return added;
            }

            throw new ArgumentException($"Row has no column '{column}'. Columns: {string.Join(", ", Columns)}");
        }
    }

    /// <summary>The cell at the given column index (original table columns only).</summary>
    public SosCell this[int index] => new(_output, _columns[index], _cells[index]);

    /// <summary>All cell values in column order (original table columns only).</summary>
    public IReadOnlyList<string> Cells => _cells;

    /// <summary>Every column name on this row: the original table columns plus any added via <see cref="AddColumn"/>.</summary>
    public IReadOnlyList<string> Columns => _columns.Concat(_addedColumns.Select(c => c.Column)).ToArray();

    /// <summary>True if this row has the named column (original or added).</summary>
    public bool HasColumn(string column) =>
        Array.IndexOf(_columns, column) >= 0 || _addedColumns.Any(c => c.Column == column);

    /// <summary>
    /// Promote an extra scalar column onto this row (the "extra columns" internal-data shape, e.g. an
    /// <c>!clrstack -r</c> register), so <c>row["rip"]</c> reads it like any other column. Throws if a
    /// column with this name already exists, so an extractor can never silently shadow a real column.
    /// </summary>
    public void AddColumn(string column, string value)
    {
        if (HasColumn(column))
        {
            throw new ArgumentException($"Row already has a column '{column}'. Columns: {string.Join(", ", Columns)}");
        }

        _addedColumns.Add(new SosCell(_output, column, value));
    }

    /// <summary>
    /// The structured sub-records attached to this row (the "array of structs" internal-data shape,
    /// e.g. the GC roots from <c>!clrstack -gc</c>). Empty unless the table was parsed with an
    /// extractor that called <see cref="AddData"/>.
    /// </summary>
    public IReadOnlyList<SosDataRow> Data => _data;

    /// <summary>Attach a structured sub-record (one parsed internal-data line) to this row.</summary>
    public void AddData(SosDataRow row) => _data.Add(row);
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>
/// One structured sub-record attached to a <see cref="SosRow"/> by a table data extractor — the
/// "array of structs" shape of internal-data rows. Each <c>!clrstack -gc</c> root line, or each
/// <c>-a</c>/<c>-l</c>/<c>-p</c> parameter/local, becomes one of these: a small bag of named
/// <see cref="SosCell"/>s with the same <c>this[column]</c> + <c>.As*()</c> ergonomics as a table
/// row, but a free-standing set of fields rather than columns shared across rows. Contrast with
/// <see cref="SosRow.AddColumn"/>, which is for the "extra scalar columns" shape (<c>-r</c>
/// registers), where each name occurs once per row.
/// </summary>
public sealed class SosDataRow
{
    private readonly string _sourceLine;
    private readonly List<SosCell> _cells = new();

    /// <summary>Create an empty record whose failure messages cite <paramref name="sourceLine"/>.</summary>
    public SosDataRow(string sourceLine)
    {
        _sourceLine = sourceLine;
    }

    /// <summary>The raw output line this record was parsed from.</summary>
    public string SourceLine => _sourceLine;

    /// <summary>All fields of this record, in insertion order.</summary>
    public IReadOnlyList<SosCell> Cells => _cells;

    /// <summary>Add a named field. Throws if <paramref name="column"/> is already present.</summary>
    public SosDataRow Add(string column, string value)
    {
        if (_cells.Any(c => c.Column == column))
        {
            throw new ArgumentException($"Data row already has a field '{column}'. Fields: {string.Join(", ", _cells.Select(c => c.Column))}");
        }

        _cells.Add(new SosCell(_sourceLine, column, value));
        return this;
    }

    /// <summary>True if this record has the named field.</summary>
    public bool Has(string column) => _cells.Any(c => c.Column == column);

    /// <summary>The field with the given name.</summary>
    public SosCell this[string column] =>
        _cells.FirstOrDefault(c => c.Column == column)
        ?? throw new ArgumentException($"Data row has no field '{column}'. Fields: {string.Join(", ", _cells.Select(c => c.Column))}");

    /// <summary>The raw source line, so a record renders usefully in any <c>Assert.*</c> failure.</summary>
    public override string ToString() => _sourceLine;
}

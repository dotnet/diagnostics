// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>
/// One cell of a <see cref="SosRow"/>. It converts implicitly to its value string (so
/// <c>row["Name"] == "System.Int32[]"</c> and <c>Assert.Equal(..., row["Name"])</c> just work) and
/// adds value helpers like <see cref="AsUInt64"/> to parse the value as a number, and
/// <see cref="Contains"/>:
/// <code>
/// SosRow row = table.Single(r =&gt; r["Name"] == "System.Int32[]");
/// ulong obj = row["Object"].AsUInt64(Sos.Addr);
/// </code>
/// Cells sliced out of a table by the parser carry the full captured output for rich failure
/// messages. Free-standing data cells (built by a table data extractor via the public
/// <see cref="SosCell(string, string, string)"/> constructor) carry only their source line, which is
/// used as the failure context instead.
/// </summary>
public sealed class SosCell
{
    private readonly SosOutput? _output;
    private readonly string? _sourceLine;

    public string Column { get; }
    public string Value { get; }

    internal SosCell(SosOutput output, string column, string value)
    {
        _output = output;
        Column = column;
        Value = value;
    }

    /// <summary>
    /// Build a free-standing cell from a raw <paramref name="sourceLine"/> — used by table data
    /// extractors to attach extra values (registers, GC roots, …) parsed out of the lines between
    /// aligned table rows. The source line becomes the failure context for the <c>As*</c> helpers.
    /// </summary>
    public SosCell(string sourceLine, string column, string value)
    {
        _sourceLine = sourceLine;
        Column = column;
        Value = value;
    }

    /// <summary>The cell value, so a cell drops straight into string comparisons / <c>Assert.Equal</c>.</summary>
    public static implicit operator string(SosCell cell) => cell.Value;

    /// <summary>Parse the value as an unsigned 64-bit number per <paramref name="token"/> (hex/decimal).</summary>
    public ulong AsUInt64(SosToken token) => SosConvert.UInt64(Column, Value, token, Fail);

    /// <summary>Parse the value as an unsigned 32-bit number per <paramref name="token"/>.</summary>
    public uint AsUInt32(SosToken token) => SosConvert.UInt32(Column, Value, token, Fail);

    /// <summary>Parse the value as an unsigned 16-bit number per <paramref name="token"/>.</summary>
    public ushort AsUInt16(SosToken token) => SosConvert.UInt16(Column, Value, token, Fail);

    /// <summary>Parse the value as a signed 64-bit number per <paramref name="token"/>.</summary>
    public long AsInt64(SosToken token) => SosConvert.Int64(Column, Value, token, Fail);

    /// <summary>Parse the value as a signed 32-bit number per <paramref name="token"/>.</summary>
    public int AsInt32(SosToken token) => SosConvert.Int32(Column, Value, token, Fail);

    /// <summary>Parse the value as a signed 16-bit number per <paramref name="token"/>.</summary>
    public short AsInt16(SosToken token) => SosConvert.Int16(Column, Value, token, Fail);

    /// <summary>Parse the value as a byte per <paramref name="token"/>.</summary>
    public byte AsByte(SosToken token) => SosConvert.Byte(Column, Value, token, Fail);

    /// <summary>Parse the value as a boolean (<c>true</c>/<c>false</c> or <c>1</c>/<c>0</c>).</summary>
    public bool AsBoolean() => SosConvert.Boolean(Column, Value, Fail);

    /// <summary>True if the value contains <paramref name="substring"/>.</summary>
    public bool Contains(string substring) => Value.Contains(substring, StringComparison.Ordinal);

    public override string ToString() => Value;

    private Exception Fail(string expectation) =>
        _output is not null
            ? _output.Fail(expectation)
            : new SosAssertException("(table data)", _sourceLine ?? string.Empty, expectation, _sourceLine ?? string.Empty);
}

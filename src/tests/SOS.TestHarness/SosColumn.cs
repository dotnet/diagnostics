// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>
/// A column spec for <see cref="SosOutput.Table"/>: a header name, an optional <see cref="SosToken"/>
/// every cell must match, and the column's alignment. SOS prints fixed-width columns that are either
/// left- or right-aligned (addresses/SPs are usually right-aligned, type names left); the parser
/// slices cells using that alignment. Implicit conversions let a left-aligned column be written as a
/// bare name or a <c>(name, token)</c> pair; right-aligned columns use <see cref="ColumnAlignment.Right"/>:
/// <code>
/// pe.Table("SP", "IP", "Function");                                          // all left, no validation
/// dso.Table(ColumnAlignment.Right("SP/REG"), ColumnAlignment.Right("Object"), "Name");
/// dso.Table(ColumnAlignment.Right("Object", Sos.Addr), "Name");              // right-aligned + validated
/// </code>
/// </summary>
public readonly struct SosColumn
{
    public SosColumn(string header, SosToken? token = null, bool rightAligned = false)
    {
        Header = header;
        Token = token;
        RightAligned = rightAligned;
    }

    /// <summary>The column header name.</summary>
    public string Header { get; }

    /// <summary>The token every cell must match, or <c>null</c> to skip validation for this column.</summary>
    public SosToken? Token { get; }

    /// <summary>True if the column's values are right-aligned under the header (default left).</summary>
    public bool RightAligned { get; }

    /// <summary>A bare, left-aligned header name with no per-cell validation.</summary>
    public static implicit operator SosColumn(string header) => new(header);

    /// <summary>A left-aligned header name paired with the token its cells must match.</summary>
    public static implicit operator SosColumn((string Header, SosToken Token) column) =>
        new(column.Header, column.Token);
}

/// <summary>
/// Builds aligned <see cref="SosColumn"/> specs. Only left and right are meaningful for SOS's
/// fixed-width output; <see cref="Left"/> is the default behavior (a bare string also yields it),
/// and <see cref="Right"/> marks a right-aligned column.
/// </summary>
public static class ColumnAlignment
{
    /// <summary>A left-aligned column (the default), optionally validated against <paramref name="token"/>.</summary>
    public static SosColumn Left(string header, SosToken? token = null) => new(header, token, rightAligned: false);

    /// <summary>A right-aligned column (e.g. addresses/SPs), optionally validated against <paramref name="token"/>.</summary>
    public static SosColumn Right(string header, SosToken? token = null) => new(header, token, rightAligned: true);
}

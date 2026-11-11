// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>
/// Claims an <em>internal data</em> line that belongs to <paramref name="row"/> — the most recent
/// aligned row (e.g. a GC root from <c>!clrstack -gc</c> or a register from <c>!clrstack -r</c>).
/// Called for each line after the first aligned row is matched. Attach values to the row either as
/// extra scalar columns (<see cref="SosRow.AddColumn"/>, the <c>-r</c> register shape — so
/// <c>row["rip"]</c> works) or as structured sub-records (<see cref="SosRow.AddData"/> with a
/// <see cref="SosDataRow"/>, the <c>-gc</c> roots shape). Return <c>true</c> to consume the line, or
/// <c>false</c> to let it fall through to normal row matching.
/// </summary>
public delegate bool SosDataExtractor(string line, SosRow row);

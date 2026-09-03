// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>
/// Output-capturing assertions over a set of <see cref="SosDataRow"/> records (the "array of structs"
/// internal-data shape — <c>!clrstack -gc</c> roots, or <c>-a</c>/<c>-l</c>/<c>-p</c>
/// parameters/locals). Prefer these over <c>Assert.Contains(records, predicate)</c>: on failure they
/// throw <see cref="SosAssertException"/> echoing the source lines of every record considered, plus a
/// human description of what was sought, instead of a bare "Filter not matched in collection".
/// </summary>
public static class SosDataRowExtensions
{
    /// <summary>Assert at least one record satisfies <paramref name="predicate"/> and return the first match.</summary>
    public static SosDataRow AssertContains(this IEnumerable<SosDataRow> records, Func<SosDataRow, bool> predicate, string description)
    {
        SosDataRow[] all = records.ToArray();
        SosDataRow? match = all.FirstOrDefault(predicate);
        if (match is null)
        {
            throw Fail(all, $"at least one record where {description}");
        }

        return match;
    }

    /// <summary>Assert exactly one record satisfies <paramref name="predicate"/> and return it.</summary>
    public static SosDataRow AssertSingle(this IEnumerable<SosDataRow> records, Func<SosDataRow, bool> predicate, string description)
    {
        SosDataRow[] all = records.ToArray();
        SosDataRow[] matches = all.Where(predicate).ToArray();
        if (matches.Length != 1)
        {
            throw Fail(all, $"exactly one record where {description} (found {matches.Length})");
        }

        return matches[0];
    }

    private static SosAssertException Fail(IEnumerable<SosDataRow> records, string expectation)
    {
        string body = string.Join('\n', records.Select(r => r.SourceLine));
        return new SosAssertException("(table data)", "records", expectation, body);
    }
}

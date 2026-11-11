// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Text.RegularExpressions;

namespace SOS.TestHarness;

/// <summary>
/// A named, reusable pattern for a class of <em>nondeterministic</em> value that
/// appears in SOS output — pointers, addresses, counts, HResults, etc. These are
/// the modern, readable replacement for the old <c>&lt;HEXVAL&gt;</c>/<c>&lt;DECVAL&gt;</c>
/// regex tokens: instead of hand-writing <c>[A-Fa-f0-9]+</c> soup, a test says the
/// field "should be a hex value" and the harness checks the shape while ignoring the
/// volatile value.
///
/// A token also knows how to <em>parse</em> its values to a number (hex vs decimal), so an exact
/// value assertion can say "this field, read as an address, equals 0x1234".
/// </summary>
public sealed class SosToken
{
    public string Name { get; }

    /// <summary>The inner regex fragment (no anchors), so tokens can be embedded in larger patterns.</summary>
    public string Pattern { get; }

    private readonly Regex _anchored;
    private readonly NumberStyles? _numberStyles;

    public SosToken(string name, string pattern, NumberStyles? numberStyles = null)
    {
        Name = name;
        Pattern = pattern;
        _anchored = new Regex("^(?:" + pattern + ")$", RegexOptions.Compiled);
        _numberStyles = numberStyles;
    }

    /// <summary>True if <paramref name="value"/> is entirely this kind of token.</summary>
    public bool Matches(string value) => _anchored.IsMatch(value.Trim());

    /// <summary>
    /// Parse <paramref name="value"/> to a number using this token's numeric style (hex for
    /// addresses/hex, decimal for counts), tolerating dbgeng backticks and thousands separators.
    /// Returns false for non-numeric tokens or unparseable input.
    /// </summary>
    public bool TryParseNumber(string value, out ulong number)
    {
        number = 0;
        if (_numberStyles is null)
        {
            return false;
        }

        string normalized = StripHexPrefix(value.Trim().Replace("`", string.Empty).Replace(",", string.Empty));
        return ulong.TryParse(normalized, _numberStyles.Value, CultureInfo.InvariantCulture, out number);
    }

    /// <summary>Signed counterpart of <see cref="TryParseNumber"/> (for <c>AsInt32</c>/<c>AsInt64</c>).</summary>
    public bool TryParseSigned(string value, out long number)
    {
        number = 0;
        if (_numberStyles is null)
        {
            return false;
        }

        string normalized = StripHexPrefix(value.Trim().Replace("`", string.Empty).Replace(",", string.Empty));
        return long.TryParse(normalized, _numberStyles.Value, CultureInfo.InvariantCulture, out number);
    }

    // NumberStyles.HexNumber doesn't accept a "0x" prefix, but SOS prints some hex columns 0x-prefixed
    // (e.g. an OS thread id), so drop it before parsing. Harmless for decimal tokens (they never carry it).
    private static string StripHexPrefix(string value) =>
        value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value.Substring(2) : value;

    public override string ToString() => $"<{Name}>";
}

/// <summary>
/// The vocabulary of nondeterministic value shapes. This is deliberately small and
/// named so assertions read like English: <c>Field("HResult", Sos.Hex)</c>.
/// </summary>
public static class Sos
{
    /// <summary>A hex value, optionally <c>0x</c>-prefixed and/or with dbgeng's backtick high/low split
    /// (e.g. <c>00007ffd`213c498a</c>, <c>0x2394</c>).</summary>
    public static readonly SosToken Hex = new("hex", "(?:0[xX])?[0-9A-Fa-f]+(?:`[0-9A-Fa-f]+)?", NumberStyles.HexNumber);

    /// <summary>A decimal value, optionally with thousands separators.</summary>
    public static readonly SosToken Dec = new("dec", "[0-9]+(?:,[0-9]+)*", NumberStyles.None);

    /// <summary>A pointer/address — same shape as <see cref="Hex"/>, but named for intent at call sites.</summary>
    public static readonly SosToken Addr = new("addr", "(?:0[xX])?[0-9A-Fa-f]+(?:`[0-9A-Fa-f]+)?", NumberStyles.HexNumber);

    /// <summary>
    /// A decimal value with optional `,`.
    /// </summary>
    public static readonly SosToken Integer = new("integer", "[0-9]+(?:,[0-9]+)*", NumberStyles.None);

    /// <summary>
    /// A range of memory hex-hex.
    /// </summary>
    public static readonly SosToken MemRange = new("memrange", "[0-9A-Fa-f]+-[0-9A-Fa-f]+", NumberStyles.HexNumber);

    /// <summary>
    /// A managed frame as SOS prints it: <c>Module!Namespace.Type.Method(args)+0xOFFSET</c>
    /// (e.g. <c>NestedExceptions!NestedExceptions.ThrowNested()+0x8a</c>).
    /// </summary>
    public static readonly SosToken ModuleFunctionWithOffset =
        new("module!function+offset", @"[^!\s]+!.+\+0x[0-9A-Fa-f]+");
}

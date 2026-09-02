// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.RegularExpressions;

namespace SOS.TestHarness;

/// <summary>
/// A single "Key: Value" field pulled from SOS output by <see cref="SosOutput.Field"/> or the
/// <see cref="SosOutput.this[string]"/> indexer. It exposes the parsed <see cref="Key"/>/
/// <see cref="Value"/>, an implicit conversion to the value string (so it drops straight into
/// <c>Assert.Equal</c>/<c>Assert.NotEmpty</c>), rich shape assertions, bool predicates, and numeric
/// parsing:
/// <code>
/// pe["Exception object"].AssertValid(Sos.Addr);                            // rich shape assert (full output on failure)
/// Assert.NotEmpty(pe["Exception type"]);                                   // presence (implicit string -> IEnumerable)
/// Assert.Equal(TestTargets.NestedExceptions.OuterMessage, pe["Message"]);  // value (implicit string)
/// Assert.True(pe["InnerException"].Contains("System.FormatException"));     // bool predicate
/// Assert.Equal(0x80131509u, pe.Field("HResult").AsUInt32(Sos.Hex));        // numeric (token says how to parse)
/// </code>
/// Predicates (<see cref="Contains(string)"/>) return plain values to compose with <c>Assert.*</c>;
/// the <c>Assert*</c> helpers throw <see cref="SosAssertException"/> (with the full captured output)
/// on failure.
/// </summary>
public sealed class SosField
{
    private readonly SosOutput _output;

    public string Key { get; }
    public string Value { get; }

    internal SosField(SosOutput output, string key, string value)
    {
        _output = output;
        Key = key;
        Value = value;
    }

    /// <summary>The field's value, so a field drops straight into <c>Assert.Equal(expected, pe["x"])</c>.</summary>
    public static implicit operator string(SosField field) => field.Value;

    /// <summary>Assert the value <em>has the shape</em> of <paramref name="token"/> (e.g. a hex address).</summary>
    public SosField AssertValid(SosToken token)
    {
        if (!token.Matches(Value))
        {
            throw _output.Fail($"field '{Key}' to be a {token} value (was \"{Value}\")");
        }

        return this;
    }

    /// <summary>Assert the value equals <paramref name="expected"/> exactly (literal text, not a regex).</summary>
    public SosField Equals(string expected)
    {
        if (!string.Equals(Value, expected, StringComparison.Ordinal))
        {
            throw _output.Fail($"field '{Key}' == \"{expected}\" (was \"{Value}\")");
        }

        return this;
    }

    public bool Contains(string substring) => Value.Contains(substring, StringComparison.Ordinal);
    public bool Contains(string substring, StringComparison comparison) => Value.Contains(substring, comparison);

    /// <summary>
    /// Assert the value contains <paramref name="substring"/>. Prefer this over
    /// <c>Assert.True(field.Contains(...))</c>: on failure it throws <see cref="SosAssertException"/>
    /// with the full captured output, instead of a bare "Expected: True".
    /// </summary>
    public SosField AssertContains(string substring)
    {
        if (!Value.Contains(substring, StringComparison.Ordinal))
        {
            throw _output.Fail($"field '{Key}' to contain \"{substring}\" (was \"{Value}\")");
        }

        return this;
    }

    /// <summary>Assert the value matches <paramref name="pattern"/> as a regex.</summary>
    public SosField MatchesRegex(string pattern)
    {
        if (!Regex.IsMatch(Value, pattern))
        {
            throw _output.Fail($"field '{Key}' to match /{pattern}/ (was \"{Value}\")");
        }

        return this;
    }

    /// <summary>Parse the value as an unsigned 64-bit number per <paramref name="token"/> (hex/decimal).</summary>
    public ulong AsUInt64(SosToken token) => SosConvert.UInt64(Key, Value, token, _output.Fail);

    /// <summary>Parse the value as an unsigned 32-bit number per <paramref name="token"/>.</summary>
    public uint AsUInt32(SosToken token) => SosConvert.UInt32(Key, Value, token, _output.Fail);

    /// <summary>Parse the value as an unsigned 16-bit number per <paramref name="token"/>.</summary>
    public ushort AsUInt16(SosToken token) => SosConvert.UInt16(Key, Value, token, _output.Fail);

    /// <summary>Parse the value as a signed 64-bit number per <paramref name="token"/>.</summary>
    public long AsInt64(SosToken token) => SosConvert.Int64(Key, Value, token, _output.Fail);

    /// <summary>Parse the value as a signed 32-bit number per <paramref name="token"/>.</summary>
    public int AsInt32(SosToken token) => SosConvert.Int32(Key, Value, token, _output.Fail);

    /// <summary>Parse the value as a signed 16-bit number per <paramref name="token"/>.</summary>
    public short AsInt16(SosToken token) => SosConvert.Int16(Key, Value, token, _output.Fail);

    /// <summary>Parse the value as a byte per <paramref name="token"/>.</summary>
    public byte AsByte(SosToken token) => SosConvert.Byte(Key, Value, token, _output.Fail);

    /// <summary>Parse the value as a boolean (<c>true</c>/<c>false</c> or <c>1</c>/<c>0</c>).</summary>
    public bool AsBoolean() => SosConvert.Boolean(Key, Value, _output.Fail);

    /// <summary>
    /// Extract the first <paramref name="token"/>-shaped value embedded in this field's value and
    /// parse it to a number — e.g. pull the inner-exception address out of
    /// "System.FormatException, Use !printexception 0000026F5649D200 to see more." The token is
    /// matched on word boundaries so spurious short hex runs inside words are ignored.
    /// </summary>
    public ulong Extract(SosToken token)
    {
        Match m = Regex.Match(Value, $@"\b(?:{token.Pattern})\b");
        if (!m.Success || !token.TryParseNumber(m.Value, out ulong number))
        {
            throw _output.Fail($"field '{Key}' to contain a {token} value to extract (was \"{Value}\")");
        }

        return number;
    }
}

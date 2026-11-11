// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.TestHarness;

/// <summary>
/// Shared value converters for <see cref="SosCell"/> and <see cref="SosField"/>. The
/// <see cref="SosToken"/> supplies the regex/number format (hex vs decimal); the method name picks
/// the .NET type (named to match <c>BitConverter</c>/<c>Convert</c>: <c>Int32</c>, <c>UInt64</c>,
/// <c>Boolean</c>, …). The caller supplies a <c>fail</c> delegate that turns an expectation message
/// into the right exception (a rich <see cref="SosAssertException"/> with the full captured output
/// for parser cells, or a line-scoped one for free-standing data cells), so the converters don't
/// depend on a <see cref="SosOutput"/> directly.
/// </summary>
internal static class SosConvert
{
    public static ulong UInt64(string name, string value, SosToken token, Func<string, Exception> fail)
    {
        if (!token.TryParseNumber(value, out ulong number))
        {
            throw fail($"'{name}' to be a parseable {token} value (was \"{value}\")");
        }

        return number;
    }

    public static long Int64(string name, string value, SosToken token, Func<string, Exception> fail)
    {
        if (!token.TryParseSigned(value, out long number))
        {
            throw fail($"'{name}' to be a parseable signed {token} value (was \"{value}\")");
        }

        return number;
    }

    public static uint UInt32(string name, string value, SosToken token, Func<string, Exception> fail) =>
        Checked<uint>(name, value, fail, () => checked((uint)UInt64(name, value, token, fail)));

    public static int Int32(string name, string value, SosToken token, Func<string, Exception> fail) =>
        Checked<int>(name, value, fail, () => checked((int)Int64(name, value, token, fail)));

    public static ushort UInt16(string name, string value, SosToken token, Func<string, Exception> fail) =>
        Checked<ushort>(name, value, fail, () => checked((ushort)UInt64(name, value, token, fail)));

    public static short Int16(string name, string value, SosToken token, Func<string, Exception> fail) =>
        Checked<short>(name, value, fail, () => checked((short)Int64(name, value, token, fail)));

    public static byte Byte(string name, string value, SosToken token, Func<string, Exception> fail) =>
        Checked<byte>(name, value, fail, () => checked((byte)UInt64(name, value, token, fail)));

    public static bool Boolean(string name, string value, Func<string, Exception> fail)
    {
        string v = value.Trim();
        if (bool.TryParse(v, out bool b))
        {
            return b;
        }

        if (v == "1")
        {
            return true;
        }

        if (v == "0")
        {
            return false;
        }

        throw fail($"'{name}' to be a boolean (was \"{value}\")");
    }

    private static T Checked<T>(string name, string value, Func<string, Exception> fail, Func<T> convert)
    {
        try
        {
            return convert();
        }
        catch (OverflowException)
        {
            throw fail($"'{name}' = \"{value}\" does not fit in {typeof(T).Name}");
        }
    }
}

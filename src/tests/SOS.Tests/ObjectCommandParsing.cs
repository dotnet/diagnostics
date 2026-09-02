// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Text.RegularExpressions;
using SOS.TestHarness;

namespace SOS.Tests;

/// <summary>
/// Structured parsers for the object/structure SOS commands: <c>!dumpobj</c>/<c>do</c>, <c>!dumpmt</c>,
/// <c>!dumpclass</c>, <c>!dumpmd</c>, <c>!ip2md</c>, <c>!dumparray</c>, <c>!dumpvc</c>, and
/// <c>!dumpdelegate</c>. Each wraps the command's <see cref="SosOutput"/> and exposes its fields as typed
/// members (and tables as <see cref="SosTable"/>), so tests can round-trip addresses across commands and
/// assert exact values rather than scraping lines. Behaviour mirrors the native implementations in
/// <c>src/SOS/Strike/strike.cpp</c> (e.g. <c>dumpobj</c>/<c>dumpvc</c>/array-element details share one
/// "Name/MethodTable/Size/File/Fields" shape; <c>dumpmt -MD</c> appends a MethodDesc slot table).
/// </summary>
internal static class ObjectCommandParsing
{
    public static DumpObjResult DumpObj(this Target target, ulong address, bool noFields = false)
    {
        string args = noFields ? $"-nofields {address:x}" : address.ToString("x");
        return new DumpObjResult(target.Sos($"dumpobj {args}"));
    }

    public static DumpObjResult DumpVc(this Target target, ulong methodTable, ulong address) =>
        new(target.Sos($"dumpvc {methodTable:x} {address:x}"));

    public static DumpMtResult DumpMt(this Target target, ulong methodTable, bool methods = false, bool all = false)
    {
        string flag = all ? "-all " : methods ? "-MD " : string.Empty;
        return new DumpMtResult(target.Sos($"dumpmt {flag}{methodTable:x}"));
    }

    public static DumpClassResult DumpClass(this Target target, ulong address) =>
        new(target.Sos($"dumpclass {address:x}"));

    public static MethodDumpResult DumpMd(this Target target, ulong methodDesc) =>
        new(target.Sos($"dumpmd {methodDesc:x}"), hasMethodDescHeader: false);

    public static MethodDumpResult Ip2md(this Target target, ulong ip) =>
        new(target.Sos($"ip2md {ip:x}"), hasMethodDescHeader: true);

    public static DumpArrayResult DumpArray(
        this Target target, ulong address, int? start = null, int? length = null, bool details = false, bool noFields = false)
    {
        string args = string.Empty;
        if (start is int s)
        {
            args += $"-start {s} ";
        }

        if (length is int l)
        {
            args += $"-length {l} ";
        }

        if (details)
        {
            args += "-details ";
        }

        if (noFields)
        {
            args += "-nofields ";
        }

        return new DumpArrayResult(target.Sos($"dumparray {args}{address:x}"));
    }

    public static DumpDelegateResult DumpDelegate(this Target target, ulong address) =>
        new(target.Sos($"dumpdelegate {address:x}"));

    /// <summary>The leading decimal of a SOS size field like <c>"56(0x38) bytes"</c>.</summary>
    internal static int ParseSize(string value)
    {
        Match m = Regex.Match(value, @"^(\d+)");
        return int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    internal static ulong Hex(string value) =>
        ulong.Parse(value.Replace("`", string.Empty).TrimStart('0', 'x', 'X') is { Length: > 0 } t ? t : "0",
            NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}

/// <summary>A row of an object/value-class <c>Fields:</c> table.</summary>
public readonly record struct ObjFieldRow(
    ulong MethodTable, uint Field, ulong Offset, string Type, bool IsValueType, string Attr, string Value, string Name);

/// <summary>Parsed <c>!dumpobj</c> / <c>!dumpvc</c> output (they share one layout). When <c>-nofields</c> is
/// used (or the type has no fields) <see cref="Fields"/> is empty.</summary>
public sealed class DumpObjResult
{
    private static readonly Regex s_fieldRow = new(
        @"^\s*(?<mt>[0-9a-fA-F`]+)\s+(?<field>[0-9a-fA-F]+)\s+(?<offset>[0-9a-fA-F]+)\s+(?<type>.*?)\s+(?<vt>Yes|No)\s+(?<attr>instance|static|shared|TLstatic)\s+(?<rest>.*\S)\s*$",
        RegexOptions.Compiled);

    public DumpObjResult(SosOutput output)
    {
        Output = output;
        Name = output["Name"].Value;
        MethodTable = output["MethodTable"].AsUInt64(Sos.Addr);
        Size = ObjectCommandParsing.ParseSize(output["Size"].Value);

        List<ObjFieldRow> fields = new();
        bool inFields = false;
        foreach (string raw in output.Lines)
        {
            string line = raw.TrimEnd();
            if (line.StartsWith("Fields:", StringComparison.Ordinal))
            {
                inFields = true;
                continue;
            }

            if (!inFields || line.Length == 0)
            {
                continue;
            }

            if (line.TrimStart().StartsWith("MT", StringComparison.Ordinal))
            {
                continue; // the column header
            }

            Match m = s_fieldRow.Match(line);
            if (!m.Success)
            {
                continue; // "None", trailing notes (e.g. thin-lock line), etc.
            }

            // The combined "rest" is either "<value> <name>" or just "<name>" (static fields blank the value).
            string rest = m.Groups["rest"].Value;
            int lastSpace = rest.LastIndexOf(' ');
            string value, name;
            if (lastSpace < 0)
            {
                value = string.Empty;
                name = rest;
            }
            else
            {
                value = rest[..lastSpace].Trim();
                name = rest[(lastSpace + 1)..];
            }

            fields.Add(new ObjFieldRow(
                ObjectCommandParsing.Hex(m.Groups["mt"].Value),
                uint.Parse(m.Groups["field"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                ObjectCommandParsing.Hex(m.Groups["offset"].Value),
                m.Groups["type"].Value.Trim(),
                m.Groups["vt"].Value == "Yes",
                m.Groups["attr"].Value,
                value,
                name));
        }

        Fields = fields;
    }

    public SosOutput Output { get; }
    public string Name { get; }
    public ulong MethodTable { get; }
    public int Size { get; }
    public IReadOnlyList<ObjFieldRow> Fields { get; }

    /// <summary>The field row whose <c>Name</c> equals <paramref name="name"/>.</summary>
    public ObjFieldRow Field(string name) =>
        Fields.FirstOrDefault(f => f.Name == name) is { Name.Length: > 0 } row
            ? row
            : throw Output.Fail($"a field named \"{name}\"");
}

/// <summary>A row of the <c>!dumpmt -MD</c> MethodDesc slot table.</summary>
public readonly record struct MethodSlot(ulong Entry, ulong MethodDesc, string Jit, ulong Slot, string Name);

/// <summary>Parsed <c>!dumpmt</c> output (with <c>-MD</c> the method slot table is populated; with
/// <c>-all</c> the "Additional Details" fields are too).</summary>
public sealed class DumpMtResult
{
    private static readonly Regex s_methodRow = new(
        @"^(?<entry>[0-9a-fA-F`]+)\s+(?<md>[0-9a-fA-F`]+)\s+(?<jit>NONE|JIT|PreJIT|HASNATIVE|\S+)\s+(?<slot>[0-9a-fA-F`]+)\s+(?<name>.+\S)\s*$",
        RegexOptions.Compiled);

    public DumpMtResult(SosOutput output)
    {
        Output = output;
        Parent = output["Parent"].AsUInt64(Sos.Addr);
        Module = output["Module"].AsUInt64(Sos.Addr);
        Name = output["Name"].Value;
        MdToken = output["mdToken"].AsUInt64(Sos.Hex);
        NumberOfMethods = int.Parse(output["Number of Methods"].Value, CultureInfo.InvariantCulture);

        // Desktop .NET Framework prints a separate EEClass (which dumpclass requires); modern .NET unifies
        // it with the method table and omits the line, so this is null there and callers fall back to the MT.
        if (output.Contains("EEClass:"))
        {
            EEClass = output["EEClass"].AsUInt64(Sos.Addr);
        }

        List<MethodSlot> methods = new();
        bool inTable = false;
        foreach (string raw in output.Lines)
        {
            string line = raw.TrimEnd();
            if (line.StartsWith("MethodDesc Table", StringComparison.Ordinal))
            {
                inTable = true;
                continue;
            }

            if (line.StartsWith("Additional Details", StringComparison.Ordinal))
            {
                inTable = false;
                continue;
            }

            if (!inTable || line.Length == 0 || line.StartsWith("---", StringComparison.Ordinal) ||
                line.TrimStart().StartsWith("Entry", StringComparison.Ordinal))
            {
                continue;
            }

            Match m = s_methodRow.Match(line);
            if (m.Success)
            {
                methods.Add(new MethodSlot(
                    ObjectCommandParsing.Hex(m.Groups["entry"].Value),
                    ObjectCommandParsing.Hex(m.Groups["md"].Value),
                    m.Groups["jit"].Value,
                    ObjectCommandParsing.Hex(m.Groups["slot"].Value),
                    m.Groups["name"].Value.Trim()));
            }
        }

        Methods = methods;

        if (output.Contains("NumInstanceFields:"))
        {
            NumInstanceFields = int.Parse(output["NumInstanceFields"].Value, CultureInfo.InvariantCulture);
            NumStaticFields = int.Parse(output["NumStaticFields"].Value, CultureInfo.InvariantCulture);
        }
    }

    public SosOutput Output { get; }
    public ulong Parent { get; }
    public ulong Module { get; }
    public string Name { get; }
    public ulong MdToken { get; }

    /// <summary>The EEClass address (desktop .NET Framework only; null on modern .NET where it equals the
    /// method table). <c>dumpclass</c> needs this on desktop and accepts the method table on modern .NET.</summary>
    public ulong? EEClass { get; }

    public int NumberOfMethods { get; }
    public IReadOnlyList<MethodSlot> Methods { get; }

    /// <summary>"Additional Details" instance-field count (only present with <c>-all</c>; else null).</summary>
    public int? NumInstanceFields { get; }
    public int? NumStaticFields { get; }
}

/// <summary>Parsed <c>!dumpclass</c> output.</summary>
public sealed class DumpClassResult
{
    public DumpClassResult(SosOutput output)
    {
        Output = output;
        ClassName = output["Class Name"].Value;
        MdToken = output["mdToken"].AsUInt64(Sos.Hex);
        Module = output["Module"].AsUInt64(Sos.Addr);
        MethodTable = output["Method Table"].AsUInt64(Sos.Addr);
    }

    public SosOutput Output { get; }
    public string ClassName { get; }
    public ulong MdToken { get; }
    public ulong Module { get; }
    public ulong MethodTable { get; }
}

/// <summary>Parsed <c>!dumpmd</c> and <c>!ip2md</c> output (ip2md adds a leading <c>MethodDesc</c> line and,
/// for jitted code, a <c>Source file</c> line).</summary>
public sealed class MethodDumpResult
{
    private static readonly Regex s_source = new(@"^Source file:\s+(?<file>.+?)\s+@\s+(?<line>\d+)\s*$", RegexOptions.Compiled);

    public MethodDumpResult(SosOutput output, bool hasMethodDescHeader)
    {
        Output = output;
        if (hasMethodDescHeader)
        {
            MethodDesc = output["MethodDesc"].AsUInt64(Sos.Addr);
        }

        MethodName = output["Method Name"].Value;
        MethodTable = output["MethodTable"].AsUInt64(Sos.Addr);
        MdToken = output["mdToken"].AsUInt64(Sos.Hex);
        Module = output["Module"].AsUInt64(Sos.Addr);
        IsJitted = string.Equals(output["IsJitted"].Value, "yes", StringComparison.OrdinalIgnoreCase);

        foreach (string raw in output.Lines)
        {
            Match m = s_source.Match(raw.TrimEnd());
            if (m.Success)
            {
                SourceFile = m.Groups["file"].Value.Trim();
                SourceLine = int.Parse(m.Groups["line"].Value, CultureInfo.InvariantCulture);
                break;
            }
        }
    }

    public SosOutput Output { get; }

    /// <summary>The MethodDesc address (only present for ip2md; dumpmd takes it as input).</summary>
    public ulong? MethodDesc { get; }
    public string MethodName { get; }
    public ulong MethodTable { get; }
    public ulong MdToken { get; }
    public ulong Module { get; }
    public bool IsJitted { get; }
    public string? SourceFile { get; }
    public int? SourceLine { get; }
}

/// <summary>One element row of <c>!dumparray</c>: its index and the printed address (the element's own
/// address for value types, or the referenced object for reference types).</summary>
public readonly record struct ArrayElement(int Index, ulong Address);

/// <summary>Parsed <c>!dumparray</c> output (header + element list; <c>-details</c> sub-dumps are left in
/// <see cref="SosOutput"/> for the test to inspect).</summary>
public sealed class DumpArrayResult
{
    private static readonly Regex s_arrayLine =
        new(@"^Array:\s+Rank\s+(?<rank>\d+),\s+Number of elements\s+(?<count>\d+),\s+Type\s+(?<type>.+\S)\s*$", RegexOptions.Compiled);
    private static readonly Regex s_element = new(@"^\[(?<i>\d+)\]\s+(?<addr>[0-9a-fA-F`]+)\s*$", RegexOptions.Compiled);

    public DumpArrayResult(SosOutput output)
    {
        Output = output;
        Name = output["Name"].Value;
        MethodTable = output["MethodTable"].AsUInt64(Sos.Addr);

        List<ArrayElement> elements = new();
        foreach (string raw in output.Lines)
        {
            string line = raw.TrimEnd();
            Match a = s_arrayLine.Match(line);
            if (a.Success)
            {
                Rank = int.Parse(a.Groups["rank"].Value, CultureInfo.InvariantCulture);
                NumberOfElements = long.Parse(a.Groups["count"].Value, CultureInfo.InvariantCulture);
                ElementType = a.Groups["type"].Value.Trim();
                continue;
            }

            // Only the top-level element lines (column 0); -details sub-dumps are indented, so skip those.
            if (raw.Length > 0 && !char.IsWhiteSpace(raw[0]))
            {
                Match e = s_element.Match(line);
                if (e.Success)
                {
                    elements.Add(new ArrayElement(
                        int.Parse(e.Groups["i"].Value, CultureInfo.InvariantCulture),
                        ObjectCommandParsing.Hex(e.Groups["addr"].Value)));
                }
            }
        }

        Elements = elements;
    }

    public SosOutput Output { get; }
    public string Name { get; }
    public ulong MethodTable { get; }
    public int Rank { get; }
    public long NumberOfElements { get; }
    public string ElementType { get; } = string.Empty;
    public IReadOnlyList<ArrayElement> Elements { get; }
}

/// <summary>One row of <c>!dumpdelegate</c>: the delegate's target object, native method, and method name.</summary>
public readonly record struct DelegateEntry(ulong Target, ulong Method, string Name);

/// <summary>Parsed <c>!dumpdelegate</c> output (a <c>Target Method Name</c> table; multicast delegates
/// list several rows).</summary>
public sealed class DumpDelegateResult
{
    private static readonly Regex s_row =
        new(@"^(?<target>[0-9a-fA-F`]+)\s+(?<method>[0-9a-fA-F`]+)\s+(?<name>.+\S)\s*$", RegexOptions.Compiled);

    public DumpDelegateResult(SosOutput output)
    {
        Output = output;
        List<DelegateEntry> entries = new();
        bool sawHeader = false;
        foreach (string raw in output.Lines)
        {
            string line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("Target", StringComparison.Ordinal) && line.Contains("Method"))
            {
                sawHeader = true;
                continue;
            }

            if (!sawHeader || line.Length == 0)
            {
                continue;
            }

            Match m = s_row.Match(line);
            if (m.Success)
            {
                entries.Add(new DelegateEntry(
                    ObjectCommandParsing.Hex(m.Groups["target"].Value),
                    ObjectCommandParsing.Hex(m.Groups["method"].Value),
                    m.Groups["name"].Value.Trim()));
            }
        }

        Entries = entries;
    }

    public SosOutput Output { get; }
    public IReadOnlyList<DelegateEntry> Entries { get; }
}

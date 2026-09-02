// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Text.RegularExpressions;
using SOS.TestHarness;

namespace SOS.Tests;

/// <summary>
/// Structured parsers for the code-inspection SOS commands: <c>!clru</c>/<c>!u</c> (annotated
/// disassembly), <c>!ehinfo</c>, <c>!gcinfo</c>, and the related <c>!dumpil</c> (MSIL). The disassembly
/// itself is architecture-specific, so <see cref="ClrUResult"/> models only the stable structure (the
/// "Normal JIT generated code" banner, method name, <c>Begin/size</c>, the instruction lines, source-line
/// annotations, and the markers that <c>-gcinfo</c>/<c>-ehinfo</c>/<c>-il</c> interleave). Behaviour follows
/// the native implementations in <c>src/SOS/Strike/strike.cpp</c> (<c>DECLARE_API(u/EHInfo/GCInfo/DumpIL)</c>).
/// <c>clru</c> needs the debugger's native disassembler, so it exists only on the dbgeng (cdb) host.
/// </summary>
internal static class CodeCommandParsing
{
    public static ClrUResult ClrU(
        this Target target, ulong address, bool noLines = false, bool offsets = false,
        bool gcInfo = false, bool ehInfo = false, bool il = false, bool map = false)
    {
        string flags = string.Concat(
            noLines ? "-n " : "", offsets ? "-o " : "", gcInfo ? "-gcinfo " : "",
            ehInfo ? "-ehinfo " : "", il ? "-il " : "", map ? "-map " : "");
        return new ClrUResult(target.Sos($"clru {flags}{address:x}"), offsets);
    }

    public static EhInfoResult EhInfo(this Target target, ulong address) =>
        new(target.Sos($"ehinfo {address:x}"));

    public static GcInfoResult GcInfo(this Target target, ulong address) =>
        new(target.Sos($"gcinfo {address:x}"));

    public static DumpIlResult DumpIl(this Target target, ulong address, bool ilPointer = false) =>
        new(target.Sos(ilPointer ? $"dumpil -i {address:x}" : $"dumpil {address:x}"));

    internal static ulong Hex(string value) =>
        ulong.Parse(value.Replace("`", string.Empty), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}

/// <summary>One disassembled instruction line from <c>!clru</c>.</summary>
public readonly record struct DisasmLine(int? Offset, ulong Address, string Bytes, string Mnemonic, string Operands);

/// <summary>Parsed <c>!clru</c> / <c>!u</c> output. Disassembly is arch-specific so only the stable
/// structure is modelled; <see cref="SosOutput"/> is kept for interleave-marker assertions.</summary>
public sealed class ClrUResult
{
    private static readonly Regex s_begin =
        new(@"^Begin\s+([0-9a-fA-F`]+),\s+size\s+([0-9a-fA-F]+)\s*$", RegexOptions.Compiled);
    private static readonly Regex s_source = new(@"@\s+(\d+):\s*$", RegexOptions.Compiled);
    private static readonly Regex s_instruction = new(
        @"^(?<addr>[0-9a-fA-F]+(?:`[0-9a-fA-F]+)?)\s+(?<bytes>[0-9a-fA-F]+)\s+(?<mn>\S+)(?:\s+(?<ops>.*\S))?\s*$",
        RegexOptions.Compiled);
    private static readonly Regex s_instructionWithOffset = new(
        @"^(?<off>[0-9a-fA-F]{4,8})\s+(?<addr>[0-9a-fA-F]+(?:`[0-9a-fA-F]+)?)\s+(?<bytes>[0-9a-fA-F]+)\s+(?<mn>\S+)(?:\s+(?<ops>.*\S))?\s*$",
        RegexOptions.Compiled);
    private readonly bool _hasOffsets;

    public ClrUResult(SosOutput output, bool hasOffsets = false)
    {
        Output = output;
        _hasOffsets = hasOffsets;
        HasNormalJitBanner = output.Contains("Normal JIT generated code");

        List<DisasmLine> instructions = new();
        int sourceLines = 0;
        foreach (string raw in output.Lines)
        {
            string line = raw.TrimEnd();

            Match b = s_begin.Match(line);
            if (b.Success)
            {
                Begin = CodeCommandParsing.Hex(b.Groups[1].Value);
                Size = (int)CodeCommandParsing.Hex(b.Groups[2].Value);
                continue;
            }

            if (s_source.IsMatch(line))
            {
                sourceLines++;
                continue;
            }

            Match m = (hasOffsets ? s_instructionWithOffset : s_instruction).Match(line);
            if (m.Success)
            {
                int? off = m.Groups["off"].Success ? (int)CodeCommandParsing.Hex(m.Groups["off"].Value) : null;
                instructions.Add(new DisasmLine(
                    off,
                    CodeCommandParsing.Hex(m.Groups["addr"].Value),
                    m.Groups["bytes"].Value,
                    m.Groups["mn"].Value,
                    m.Groups["ops"].Value.Trim()));
                continue;
            }

            // The method name is the non-empty line that precedes Begin and isn't the banner.
            if (line.Length > 0 && Begin == 0 && line != "Normal JIT generated code")
            {
                MethodName = line.Trim();
            }
        }

        Instructions = instructions;
        SourceLineCount = sourceLines;
    }

    public SosOutput Output { get; }
    public bool HasNormalJitBanner { get; }
    public string MethodName { get; private set; } = string.Empty;
    public ulong Begin { get; private set; }
    public int Size { get; private set; }
    public IReadOnlyList<DisasmLine> Instructions { get; }

    /// <summary>How many <c>file @ line:</c> source annotations were printed (0 with <c>-n</c>).</summary>
    public int SourceLineCount { get; }

    /// <summary>True if the instruction lines carry the <c>-o</c> offset prefix.</summary>
    public bool HasOffsets => _hasOffsets && Instructions.Count > 0 && Instructions.All(i => i.Offset is not null);
}

/// <summary>One exception-handling clause from <c>!ehinfo</c>.</summary>
public readonly record struct EhClause(int Index, string Kind, ulong ClauseStart, ulong ClauseEnd, ulong HandlerStart, ulong HandlerEnd);

/// <summary>Parsed <c>!ehinfo</c> output: the method identity plus its EH clauses (empty for a method with
/// no try/catch).</summary>
public sealed class EhInfoResult
{
    private static readonly Regex s_handler = new(@"^EHHandler\s+(\d+):\s+(?<kind>.+?)\s*$", RegexOptions.Compiled);
    private static readonly Regex s_range =
        new(@"^(?<label>Clause|Handler):\s+\[(?<start>[0-9a-fA-F`]+),\s*(?<end>[0-9a-fA-F`]+)\]", RegexOptions.Compiled);

    public EhInfoResult(SosOutput output)
    {
        Output = output;
        MethodDesc = output["MethodDesc"].AsUInt64(Sos.Addr);
        MethodName = output["Method Name"].Value;

        List<EhClause> clauses = new();
        int index = 0;
        string kind = string.Empty;
        ulong cs = 0, ce = 0, hs = 0, he = 0;
        bool open = false;

        void Flush()
        {
            if (open)
            {
                clauses.Add(new EhClause(index, kind, cs, ce, hs, he));
                open = false;
            }
        }

        foreach (string raw in output.Lines)
        {
            string line = raw.TrimEnd();
            Match h = s_handler.Match(line);
            if (h.Success)
            {
                Flush();
                index = int.Parse(h.Groups[1].Value, CultureInfo.InvariantCulture);
                kind = h.Groups["kind"].Value.Trim();
                cs = ce = hs = he = 0;
                open = true;
                continue;
            }

            Match r = s_range.Match(line);
            if (r.Success && open)
            {
                ulong start = CodeCommandParsing.Hex(r.Groups["start"].Value);
                ulong end = CodeCommandParsing.Hex(r.Groups["end"].Value);
                if (r.Groups["label"].Value == "Clause")
                {
                    cs = start;
                    ce = end;
                }
                else
                {
                    hs = start;
                    he = end;
                }
            }
        }

        Flush();
        Clauses = clauses;
    }

    public SosOutput Output { get; }
    public ulong MethodDesc { get; }
    public string MethodName { get; }
    public IReadOnlyList<EhClause> Clauses { get; }
}

/// <summary>Parsed <c>!gcinfo</c> output: the entry point, GC-info address, code size, and the GC transition
/// lines (interruptibility ranges and register live/dead markers).</summary>
public sealed class GcInfoResult
{
    private static readonly Regex s_entry = new(@"^entry point\s+([0-9a-fA-F`]+)", RegexOptions.Compiled);
    private static readonly Regex s_gcinfo = new(@"^GC info\s+([0-9a-fA-F`]+)", RegexOptions.Compiled);
    private static readonly Regex s_codeSize = new(
        @"^(?:Code size:\s+(?<decimal>\d+)|method\s+size\s*=\s*(?<hex>[0-9a-fA-F]+))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex s_transition =
        new(@"^[0-9a-fA-F]{4,}\s+(interruptible|not interruptible|[+\-].+|reg .+ becoming (?:live|dead))$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public GcInfoResult(SosOutput output)
    {
        Output = output;
        List<string> transitions = new();
        foreach (string raw in output.Lines)
        {
            string line = raw.Trim();
            Match e = s_entry.Match(line);
            if (e.Success)
            {
                EntryPoint = CodeCommandParsing.Hex(e.Groups[1].Value);
                continue;
            }

            Match g = s_gcinfo.Match(line);
            if (g.Success)
            {
                GcInfoAddress = CodeCommandParsing.Hex(g.Groups[1].Value);
                continue;
            }

            Match c = s_codeSize.Match(line);
            if (c.Success)
            {
                CodeSize = c.Groups["decimal"].Success
                    ? int.Parse(c.Groups["decimal"].Value, CultureInfo.InvariantCulture)
                    : int.Parse(c.Groups["hex"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                continue;
            }

            int encodedPrefix = line.LastIndexOf('|');
            string transition = encodedPrefix >= 0 ? line[(encodedPrefix + 1)..].Trim() : line;
            if (s_transition.IsMatch(transition))
            {
                transitions.Add(transition);
            }
        }

        Transitions = transitions;
    }

    public SosOutput Output { get; }
    public ulong EntryPoint { get; }
    public ulong GcInfoAddress { get; }
    public int CodeSize { get; }

    /// <summary>The interruptibility / register-liveness transition lines (e.g. "interruptible", "+rcx").</summary>
    public IReadOnlyList<string> Transitions { get; }
}

/// <summary>One MSIL instruction from <c>!dumpil</c>.</summary>
public readonly record struct IlInstruction(int Offset, string OpCode, string Operands);

/// <summary>Parsed <c>!dumpil</c> output.</summary>
public sealed class DumpIlResult
{
    private static readonly Regex s_ilAddr = new(@"^ilAddr\s+=\s+([0-9a-fA-F`]+)", RegexOptions.Compiled);
    private static readonly Regex s_instruction =
        new(@"^IL_(?<off>[0-9a-fA-F]+):\s+(?<op>\S+)(?:\s+(?<ops>.*\S))?\s*$", RegexOptions.Compiled);

    public DumpIlResult(SosOutput output)
    {
        Output = output;
        List<IlInstruction> instructions = new();
        foreach (string raw in output.Lines)
        {
            string line = raw.TrimEnd();
            Match a = s_ilAddr.Match(line);
            if (a.Success)
            {
                IlAddress = CodeCommandParsing.Hex(a.Groups[1].Value);
                continue;
            }

            Match m = s_instruction.Match(line);
            if (m.Success)
            {
                instructions.Add(new IlInstruction(
                    (int)CodeCommandParsing.Hex(m.Groups["off"].Value),
                    m.Groups["op"].Value,
                    m.Groups["ops"].Value.Trim()));
            }
        }

        Instructions = instructions;
    }

    public SosOutput Output { get; }
    public ulong IlAddress { get; }
    public IReadOnlyList<IlInstruction> Instructions { get; }
}

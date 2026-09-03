using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using SOS.TestHarness;
using Xunit;

namespace SOS.Tests;

/// <summary>
/// Which slots <see cref="TargetExtensions.ClrstackArgsLocals"/> asks SOS to print: parameters
/// (<c>-p</c>), locals (<c>-l</c>), or both (<c>-a</c>). Replaces passing the raw flag string, so a
/// typo is a compile error rather than a runtime "OS Thread Id:" assertion failure.
/// </summary>
internal enum ArgsLocals
{
    Parameters,
    Locals,
    Both,
}

internal static partial class TargetExtensions
{
    /// <summary>
    /// Navigate the target to its first defined stop point, regardless of kind: a crash target runs
    /// to its crash, a snapshot target loads/runs to its first marker. Lets a theory drive a mixed
    /// set of targets (e.g. NestedException + GcPromotion) without hard-coding each one's stops.
    /// </summary>
    public static void GoToFirstStop(this Target self)
    {
        StopPoint stop = TargetCatalog.Get(self.TargetName).StopPoints[0];
        if (stop.Kind == StopKind.Crash)
        {
            self.GoToCrash();
        }
        else
        {
            self.GoToStopPoint(stop.Name);
        }
    }

    public static SosTable ClrstackRegisters(this Target self)
    {
        SosOutput clrstack = self.Sos("clrstack -r");

        // Structural shape the legacy scripts checked: the thread banner is present and the table
        // parses with the Child SP / IP / Call Site header (Table throws if the header is missing).
        Assert.True(clrstack.Contains("OS Thread Id:"), "clrstack -r output had no 'OS Thread Id:' banner.");

        SosTable table = clrstack.Table([ColumnAlignment.Right("Child SP"), ColumnAlignment.Right("IP"), "Call Site"], dataExtractor: ExtractData);

        // Child SP and IP are addresses; every frame has a (non-empty) Call Site.
        table.AssertValid(Sos.Addr, Sos.Addr, new SosToken("not-empty", @".+"));
        Assert.NotEmpty(table);

        // -r registers are promoted onto the row as extra columns; every frame prints a block, so
        // every row must carry at least the instruction-pointer register column.
        Assert.All(table, r => Assert.Contains(r.Columns, c => s_ipRegisters.Contains(c, StringComparer.OrdinalIgnoreCase)));

        return table;

        static bool ExtractData(string line, SosRow row)
        {
            if (!row.HasColumn("InternalFrame"))
                AddStackExtraData(row);

            MatchCollection matches = s_registerRegex.Matches(line);
            foreach (Match m in matches)
                row.AddColumn(m.Groups[1].Value, m.Groups[2].Value);

            return matches.Count > 0;
        }
    }

    // The instruction-pointer register names SOS prints across platforms (amd64 rip, x86 eip,
    // arm/arm64 pc); used to find the per-frame IP register column added by the -r extractor.
    private static readonly string[] s_ipRegisters = ["rip", "eip", "pc"];

    /// <summary>
    /// Run plain <c>!clrstack</c> (or <c>!clrstack -n</c> when <paramref name="suppressLines"/> is set)
    /// and parse the Child SP / IP / Call Site table, splitting each Call Site into the same
    /// InternalFrame / Function / SourceFile / LineNumber columns the <c>-r</c> path adds. With <c>-n</c>
    /// SOS omits the <c>[file @ line]</c> annotation, so SourceFile / LineNumber come back empty.
    /// </summary>
    public static SosTable Clrstack(this Target self, bool suppressLines = false) =>
        ParseFrameTable(self.Sos(suppressLines ? "clrstack -n" : "clrstack"));

    /// <summary>
    /// Run <c>!clrstack -c &lt;count&gt;</c> (limit the number of printed frames) and parse the frame
    /// table. SOS counts every printed row toward the limit, internal frames included.
    /// </summary>
    public static SosTable ClrstackFrames(this Target self, int count) =>
        ParseFrameTable(self.Sos($"clrstack -c {count}"));

    /// <summary>Parse the Child SP / IP / Call Site frame table and split each Call Site.</summary>
    private static SosTable ParseFrameTable(SosOutput output)
    {
        Assert.True(output.Contains("OS Thread Id:"), "clrstack output had no 'OS Thread Id:' banner.");

        SosTable table = output.Table([ColumnAlignment.Right("Child SP"), ColumnAlignment.Right("IP"), "Call Site"]);
        table.AssertValid(Sos.Addr, Sos.Addr, new SosToken("not-empty", @".+"));
        Assert.NotEmpty(table);

        foreach (SosRow row in table)
            AddStackExtraData(row);

        return table;
    }

    /// <summary>
    /// One frame of <c>!clrstack -f</c> (full / native-interleaved). Unlike plain clrstack, an
    /// internal frame here has a blank IP, and frames are a mix of managed
    /// (<c>Assembly.dll!Ns.Method(args) + offset [file @ line]</c>) and native
    /// (<c>module!symbol + offset</c> / <c>module + offset</c>) frames — so this is parsed line by
    /// line rather than through the aligned-table parser (which requires every column present).
    /// </summary>
    public sealed record FullFrame(string ChildSP, string IP, string CallSite)
    {
        /// <summary>An internal clr!Frame marker like <c>[InlinedCallFrame: …]</c>.</summary>
        public bool IsInternal => CallSite.StartsWith('[');

        /// <summary>The call site without the trailing <c>[file @ line]</c> source annotation.</summary>
        public string Function
        {
            get
            {
                int src = CallSite.LastIndexOf(" [", StringComparison.Ordinal);
                return (src >= 0 ? CallSite[..src] : CallSite).Trim();
            }
        }

        /// <summary>A managed frame is assembly-qualified: <c>Assembly.dll!…</c> / <c>Assembly.exe!…</c>.</summary>
        public bool IsManaged =>
            Function.Contains(".dll!", StringComparison.OrdinalIgnoreCase) ||
            Function.Contains(".exe!", StringComparison.OrdinalIgnoreCase);

        /// <summary>True if this frame is in one of the native runtime/OS modules (cdb only).</summary>
        public bool IsNativeRuntime =>
            !IsInternal && !IsManaged &&
            s_nativeModules.Any(m => Function.Contains(m, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly string[] s_nativeModules =
        ["coreclr", "clr!", "clr+", "ntdll", "kernel32", "kernelbase", "hostfxr", "hostpolicy", "mscoree"];

    /// <summary>
    /// Run <c>!clrstack -f</c> and parse its frames. Each frame line starts with a hex Child SP at
    /// column 0; the IP and Call Site are sliced at the header's column positions (IP is blank for
    /// internal frames). See <see cref="FullFrame"/>.
    /// </summary>
    public static IReadOnlyList<FullFrame> ClrstackFull(this Target self)
    {
        SosOutput output = self.Sos("clrstack -f");
        Assert.True(output.Contains("OS Thread Id:"), "clrstack -f output had no 'OS Thread Id:' banner.");

        string[] lines = output.Text.Replace("\r", string.Empty).Split('\n');
        List<FullFrame> frames = ParseFrameLines(lines, 0, lines.Length);
        Assert.NotEmpty(frames);
        return frames;
    }

    /// <summary>One thread's stack from <c>!clrstack -all</c>.</summary>
    public sealed record ThreadStack(string OsThreadId, IReadOnlyList<FullFrame> Frames);

    /// <summary>
    /// Run <c>!clrstack -all</c> (every managed thread's stack) and parse it into one
    /// <see cref="ThreadStack"/> per <c>OS Thread Id:</c> banner, reusing the same frame-line parser as
    /// <see cref="ClrstackFull"/>. A thread with no managed user frames still appears (with only an
    /// internal frame).
    /// </summary>
    public static IReadOnlyList<ThreadStack> ClrstackAllThreads(this Target self)
    {
        SosOutput output = self.Sos("clrstack -all");
        string[] lines = output.Text.Replace("\r", string.Empty).Split('\n');

        // The line index of each "OS Thread Id:" banner, which delimits the per-thread sections.
        List<int> banners = new();
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("OS Thread Id:", StringComparison.Ordinal))
                banners.Add(i);
        }

        Assert.NotEmpty(banners);

        List<ThreadStack> threads = new();
        for (int b = 0; b < banners.Count; b++)
        {
            int start = banners[b];
            int end = b + 1 < banners.Count ? banners[b + 1] : lines.Length;
            string id = lines[start].Trim()["OS Thread Id:".Length..].Trim().Split(' ')[0];
            threads.Add(new ThreadStack(id, ParseFrameLines(lines, start, end)));
        }

        return threads;
    }

    /// <summary>
    /// Parse the frames of one Child SP / IP / Call Site table found within <c>lines[from..to)</c>.
    /// Frame rows start with a hex Child SP at column 0; Child SP and IP are right-aligned (values
    /// extend left of their header) while Call Site is left-aligned, and IP is blank for internal
    /// frames. Returns an empty list if the block has no header (a thread with no managed stack).
    /// </summary>
    private static List<FullFrame> ParseFrameLines(string[] lines, int from, int to)
    {
        List<FullFrame> frames = new();
        int header = -1;
        for (int i = from; i < to; i++)
        {
            if (lines[i].Contains("Child SP") && lines[i].Contains("IP") && lines[i].Contains("Call Site"))
            {
                header = i;
                break;
            }
        }

        if (header < 0)
            return frames;

        int childSpEnd = lines[header].IndexOf("Child SP", StringComparison.Ordinal) + "Child SP".Length;
        int csCol = lines[header].IndexOf("Call Site", StringComparison.Ordinal);

        for (int i = header + 1; i < to; i++)
        {
            string line = lines[i];
            if (line.Length == 0 || !Uri.IsHexDigit(line[0]))
                continue;

            string childSp = line[..Math.Min(childSpEnd, line.Length)].Trim();
            string ip = line.Length > childSpEnd ? line[childSpEnd..Math.Min(csCol, line.Length)].Trim() : string.Empty;
            string callSite = line.Length > csCol ? line[csCol..].Trim() : string.Empty;
            frames.Add(new FullFrame(childSp, ip, callSite));
        }

        return frames;
    }

    /// <summary>One managed variable from <c>!clrstack -i -a</c>: a parameter or local.</summary>
    /// <param name="Type">The declared type (e.g. <c>int</c>, <c>ArgUniqueMarker</c>, <c>string</c>).</param>
    /// <param name="Name">The variable name (ICorDebug recovers real names, unlike the non-i path).</param>
    /// <param name="Value">The decoded value for a primitive/string (e.g. <c>42</c>, <c>"argslocals"</c>); "" for an object.</param>
    /// <param name="Address">The object address when the variable is a reference; 0 otherwise.</param>
    /// <param name="HasAddress">True when the variable was printed as <c>@ 0x…</c> (a reference).</param>
    /// <param name="IsError">True for a <c>(Error 0x… retrieving …)</c> entry (slot unavailable).</param>
    public sealed record IcorVar(string Type, string Name, string Value, ulong Address, bool HasAddress, bool IsError);

    /// <summary>One frame of <c>!clrstack -i</c> (ICorDebug). Managed frames are marked <c>[DEFAULT]</c>.</summary>
    public sealed record IcorFrame(string CallSite, IReadOnlyList<IcorVar> Parameters, IReadOnlyList<IcorVar> Locals)
    {
        public bool IsManaged => CallSite.Contains("[DEFAULT]", StringComparison.Ordinal);
    }

    /// <summary>
    /// Run the EXPERIMENTAL <c>!clrstack -i</c> (ICorDebug) — optionally with <c>-a</c> to also dump
    /// each managed frame's parameters and locals — and parse its distinct output: a banner, a
    /// Child SP / IP / Call Site listing where managed frames read
    /// <c>[DEFAULT] [hasThis] &lt;ret&gt; &lt;Type.Method&gt;(&lt;sig&gt;) (&lt;module&gt;)</c>, and (with
    /// <c>-a</c>) <c>PARAMETERS:</c>/<c>LOCALS:</c> blocks of <c>+ &lt;type&gt; &lt;name&gt; @ 0x&lt;addr&gt;</c>,
    /// <c>+ &lt;type&gt; &lt;name&gt; = &lt;value&gt;</c>, or <c>+ (Error … '&lt;name&gt;')</c> lines.
    /// </summary>
    public static IReadOnlyList<IcorFrame> ClrstackICorDebug(this Target self, bool variables)
    {
        SosOutput output = self.Sos(variables ? "clrstack -i -a" : "clrstack -i");
        string[] lines = output.Text.Replace("\r", string.Empty).Split('\n');

        int header = Array.FindIndex(lines, l => l.Contains("Child SP") && l.Contains("IP") && l.Contains("Call Site"));
        Assert.True(header >= 0, $"clrstack -i had no listing header:\n{output.Text}");

        List<IcorFrame> frames = new();
        List<IcorVar>? parameters = null;
        List<IcorVar>? locals = null;
        string? callSite = null;
        List<IcorVar>? current = null;

        void Flush()
        {
            if (callSite is not null)
                frames.Add(new IcorFrame(callSite, parameters ?? [], locals ?? []));
        }

        for (int i = header + 1; i < lines.Length; i++)
        {
            string line = lines[i];
            string t = line.Trim();
            if (t.Length == 0)
                continue;

            if (t.StartsWith("Stack walk complete", StringComparison.Ordinal) || t.StartsWith("===", StringComparison.Ordinal))
                break;

            if (line.Length > 0 && Uri.IsHexDigit(line[0]))
            {
                // New frame line: "<sp> <ip|(null)> <call site...>".
                Flush();
                string[] parts = line.Split((char[]?)null, 3, StringSplitOptions.RemoveEmptyEntries);
                callSite = parts.Length >= 3 ? parts[2] : (parts.Length == 2 ? string.Empty : string.Empty);
                parameters = null;
                locals = null;
                current = null;
                continue;
            }

            if (t.StartsWith("PARAMETERS:", StringComparison.Ordinal))
            {
                parameters = new List<IcorVar>();
                current = t.Contains("(none)", StringComparison.Ordinal) ? null : parameters;
                continue;
            }

            if (t.StartsWith("LOCALS:", StringComparison.Ordinal))
            {
                locals = new List<IcorVar>();
                current = t.Contains("(none)", StringComparison.Ordinal) ? null : locals;
                continue;
            }

            if (t.StartsWith("+", StringComparison.Ordinal) && current is not null)
                current.Add(ParseIcorVar(t[1..].Trim()));
        }

        Flush();
        Assert.NotEmpty(frames);
        return frames;
    }

    private static IcorVar ParseIcorVar(string body)
    {
        if (body.StartsWith("(Error", StringComparison.Ordinal))
        {
            Match e = Regex.Match(body, @"'(?<name>[^']+)'");
            return new IcorVar(string.Empty, e.Success ? e.Groups["name"].Value : string.Empty, string.Empty, 0, false, true);
        }

        int at = body.IndexOf(" @ ", StringComparison.Ordinal);
        if (at >= 0)
        {
            (string type, string name) = SplitTypeName(body[..at]);
            ulong addr = ParseHexLoose(body[(at + 3)..].Trim());
            return new IcorVar(type, name, string.Empty, addr, true, false);
        }

        int eq = body.IndexOf(" = ", StringComparison.Ordinal);
        if (eq >= 0)
        {
            (string type, string name) = SplitTypeName(body[..eq]);
            return new IcorVar(type, name, body[(eq + 3)..].Trim(), 0, false, false);
        }

        (string ty, string nm) = SplitTypeName(body);
        return new IcorVar(ty, nm, string.Empty, 0, false, false);
    }

    // "int number" / "ArgUniqueMarker arg" -> (type, name): the name is the last whitespace token.
    private static (string Type, string Name) SplitTypeName(string s)
    {
        s = s.Trim();
        int sp = s.LastIndexOf(' ');
        return sp < 0 ? (string.Empty, s) : (s[..sp].Trim(), s[(sp + 1)..].Trim());
    }

    private static ulong ParseHexLoose(string s)
    {
        s = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s[2..] : s;
        return ulong.TryParse(s, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out ulong v) ? v : 0;
    }

    private static void AddStackExtraData(SosRow row)
    {
        string callSite = row["Call Site"].Value;
        bool internalFrame = callSite.StartsWith('[');
        row.AddColumn("InternalFrame", internalFrame.ToString());

        if (!internalFrame && TryParseCallsite(callSite, out (string Function, string SourceFile, string LineNumber) data))
        {
            row.AddColumn("Function", data.Function);
            row.AddColumn("SourceFile", data.SourceFile);
            row.AddColumn("LineNumber", data.LineNumber);
        }
        else
        {
            row.AddColumn("Function", "");
            row.AddColumn("SourceFile", "");
            row.AddColumn("LineNumber", "");
        }
    }

    public static bool TryParseCallsite(string callsite, out (string Function, string Path, string Line) result)
    {
        var match = s_callsiteRegex.Match(callsite);
        result = !match.Success ? default :
            (
                match.Groups["function"].Value,
                match.Groups["path"].Success ? match.Groups["path"].Value : "",
                match.Groups["line"].Success ? match.Groups["line"].Value : ""
            );

        return match.Success;
    }

    /// <summary>
    /// Run <c>!clrstack -gc</c> and parse it into the Child SP / IP / Call Site table, attaching each
    /// frame's GC roots as structured <see cref="SosDataRow"/> sub-records in <see cref="SosRow.Data"/>.
    /// A root line (printed by strike.cpp BuildRegisterOutput + PrintRef) has the optional shape
    /// <c>[reg[+/-off]: ][addr -&gt; ]obj[ (pinned)][ (interior)][ - Type]</c>, so each record carries:
    /// Register, Offset, Address (all "" when absent), Object (always; "0…0" for null), Type ("" when
    /// the object is null or interior, since SOS only prints a type otherwise), and Pinned / Interior
    /// (always "True"/"False" — they default to False when the flag isn't printed).
    /// </summary>
    public static SosTable ClrstackGcRoots(this Target self)
    {
        SosOutput clrstack = self.Sos("clrstack -gc");

        Assert.True(clrstack.Contains("OS Thread Id:"), "clrstack -gc output had no 'OS Thread Id:' banner.");

        SosTable table = clrstack.Table([ColumnAlignment.Right("Child SP"), ColumnAlignment.Right("IP"), "Call Site"], dataExtractor: ExtractRoot);
        table.AssertValid(Sos.Addr, Sos.Addr, new SosToken("not-empty", @".+"));
        Assert.NotEmpty(table);

        return table;

        static bool ExtractRoot(string line, SosRow row)
        {
            // Roots are indented into the Call Site column (columns 0/1 blank); frame rows start with
            // a hex Child SP at column 0. Only indented, non-blank lines are candidate roots.
            if (line.Length == 0 || !char.IsWhiteSpace(line[0]))
                return false;

            string content = line.Trim();
            if (content.Length == 0)
                return false;

            Match m = s_gcRootRegex.Match(content);
            if (!m.Success)
                throw new InvalidOperationException($"Unrecognized !clrstack -gc root line: \"{content}\"");

            SosDataRow root = new(line);
            root.Add("Register", Group(m, "reg"));
            root.Add("Offset", Group(m, "offset"));
            root.Add("Address", Group(m, "address"));
            root.Add("Object", m.Groups["object"].Value);
            root.Add("Type", Group(m, "type").Trim());
            root.Add("Pinned", m.Groups["pinned"].Success.ToString());
            root.Add("Interior", m.Groups["interior"].Success.ToString());
            row.AddData(root);
            return true;
        }
    }

    private static string Group(Match m, string name) => m.Groups[name].Success ? m.Groups[name].Value : "";

    /// <summary>
    /// Run <c>!clrstack</c> with one of <c>-p</c>/<c>-l</c>/<c>-a</c> and parse it into the Child SP /
    /// IP / Call Site table, attaching each frame's parameters and/or locals as structured
    /// <see cref="SosDataRow"/> records (one per <c>PARAMETERS:</c>/<c>LOCALS:</c> entry). Each record
    /// carries: Section (PARAMETERS or LOCALS), Name (the arg name, "" for locals — SOS can't recover
    /// local names), Location ("&lt;CLR reg&gt;" or the slot address as hex), Value (the slot bytes as
    /// hex), and HasData ("False" for a <c>&lt;no data&gt;</c> entry). The frame rows are split into the
    /// usual Function / SourceFile / LineNumber columns too.
    /// </summary>
    public static SosTable ClrstackArgsLocals(this Target self, ArgsLocals which)
    {
        string flag = which switch
        {
            ArgsLocals.Parameters => "-p",
            ArgsLocals.Locals => "-l",
            ArgsLocals.Both => "-a",
            _ => throw new ArgumentOutOfRangeException(nameof(which), which, "Unknown clrstack args/locals option."),
        };

        SosOutput output = self.Sos($"clrstack {flag}");
        Assert.True(output.Contains("OS Thread Id:"), $"clrstack {flag} output had no 'OS Thread Id:' banner.");

        string section = string.Empty;
        SosTable table = output.Table(
            [ColumnAlignment.Right("Child SP"), ColumnAlignment.Right("IP"), "Call Site"],
            dataExtractor: Extract);

        // Same shape check the sibling clrstack parsers (ParseFrameTable / ClrstackRegisters) apply:
        // Child SP and IP are addresses and every frame has a non-empty Call Site.
        table.AssertValid(Sos.Addr, Sos.Addr, new SosToken("not-empty", @".+"));
        Assert.NotEmpty(table);

        foreach (SosRow row in table)
            AddStackExtraData(row);

        return table;

        bool Extract(string line, SosRow row)
        {
            // Frame rows start at column 0; let normal row matching handle them, and reset the
            // current section so the next frame's entries don't inherit it.
            if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
            {
                section = string.Empty;
                return false;
            }

            string t = line.Trim();
            if (t.Length == 0)
                return true; // blank line between frames — consume so the table doesn't end here

            if (t == "PARAMETERS:") { section = "PARAMETERS"; return true; }
            if (t == "LOCALS:") { section = "LOCALS"; return true; }

            row.AddData(ParseArgLocal(line, section, t));
            return true;
        }
    }

    private static SosDataRow ParseArgLocal(string sourceLine, string section, string trimmed)
    {
        SosDataRow rec = new(sourceLine);
        rec.Add("Section", section);

        // Forms SOS prints (ShowArgs / ShowLocals):
        //   PARAMETERS:  "<name> (<loc>) = <value>" | "<name> = <no data>" | "(<loc>) = <value>"
        //   LOCALS:      "<loc> = <value>" | "<no data>"
        // where <loc> is an address ("0x...") or "<CLR reg>", and <value> is "0x..." or "<no data>".
        // Split on the " = " that separates the location/name side from the value.
        string lhs, value;
        int eq = trimmed.IndexOf(" = ", StringComparison.Ordinal);
        if (eq >= 0)
        {
            lhs = trimmed[..eq].Trim();
            value = trimmed[(eq + 3)..].Trim();
        }
        else
        {
            lhs = string.Empty;
            value = trimmed; // bare "<no data>"
        }

        string name = string.Empty;
        string location = string.Empty;
        int paren = lhs.IndexOf('(');
        if (paren >= 0)
        {
            int close = lhs.IndexOf(')', paren);
            name = lhs[..paren].Trim();
            location = lhs[(paren + 1)..close].Trim();
        }
        else if (lhs == "<CLR reg>" || lhs.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            location = lhs; // a local: just a location, no name
        }
        else
        {
            name = lhs; // a parameter with a name but no printed location (e.g. "this")
        }

        bool hasData = value != "<no data>";
        rec.Add("Name", name);
        rec.Add("Location", location == "<CLR reg>" ? location : StripHexPrefix(location));
        rec.Add("Value", hasData ? StripHexPrefix(value) : string.Empty);
        rec.Add("HasData", hasData.ToString());
        return rec;
    }

    // SOS prints arg/local values 0x-prefixed; store the bare hex so SosCell.As*(Sos.Hex) parses it.
    private static string StripHexPrefix(string s) =>
        s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s[2..] : s;

    /// <summary>
    /// Run <c>!dumpstackobjects</c> (alias <c>dso</c>) and parse its <c>SP/REG</c>, <c>Object</c>,
    /// <c>Name</c> table — the objects SOS finds by scanning the current thread's stack memory. SP/REG
    /// and Object are right-aligned; Name is the type. Used as an SOS-native oracle: cross-check
    /// <c>!clrstack -gc</c> stack roots against it, or locate an object of a known type by name.
    /// </summary>
    public static SosTable DumpStackObjects(this Target self) =>
        self.Sos("DumpStackObjects").Table(ColumnAlignment.Right("SP/REG"), ColumnAlignment.Right("Object"), "Name");

    /// <summary>
    /// Find the address of the single instance of a uniquely-named type via <c>!dumpheap -type</c>,
    /// using the two tables SOS prints: match the type by Class Name in the <b>Statistics</b> table to
    /// get its MethodTable (asserting exactly one instance), then find that MT in the <b>object</b>
    /// table to get the address. This is the SOS-native value oracle (no ClrMD).
    /// </summary>
    public static ulong FindUniqueObject(this Target self, string typeName)
    {
        DumpHeapResult dump = self.DumpHeap($"-type {typeName}");

        SosRow stat = dump.Statistics.Single(r => r["Class Name"].Value == typeName);
        Assert.Equal(1, stat["Count"].AsInt32(Sos.Integer));
        ulong mt = stat["MT"].AsUInt64(Sos.Addr);

        SosRow obj = dump.Objects.Single(r => r["MT"].AsUInt64(Sos.Addr) == mt);
        return obj["Address"].AsUInt64(Sos.Addr);
    }

    private static readonly Regex s_registerRegex = RegisterValue();
    private static readonly Regex s_callsiteRegex = CallsiteRegex();
    private static readonly Regex s_gcRootRegex = GcRootRegex();

    [GeneratedRegex(@"(\w+)=([0-9a-f]+)", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex RegisterValue();

    [GeneratedRegex(@"^(?<function>.+?)(?:\s\[(?<path>.+)\s@\s(?<line>\d+)\])?$", RegexOptions.Compiled)]
    private static partial Regex CallsiteRegex();

    // One GC root as printed by !clrstack -gc:
    //   [<reg>[+/-<hex offset>]: ][<address> -> ]<object>[ (pinned)][ (interior)][ - <Type>]
    [GeneratedRegex(@"^(?:(?<reg>[A-Za-z]\w*)(?<offset>[+-][0-9A-Fa-f`]+)?:\s+)?(?:(?<address>[0-9A-Fa-f`]+)\s+->\s+)?(?<object>[0-9A-Fa-f`]+)(?<pinned>\s+\(pinned\))?(?<interior>\s+\(interior\))?(?:\s+-\s+(?<type>.+))?$", RegexOptions.Compiled)]
    private static partial Regex GcRootRegex();
}

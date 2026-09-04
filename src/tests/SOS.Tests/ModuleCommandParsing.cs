// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Text.RegularExpressions;
using SOS.TestHarness;

namespace SOS.Tests;

/// <summary>
/// Structured parsers for the module-keyed SOS commands: <c>!dumpdomain</c>, <c>!dumpassembly</c>,
/// <c>!dumpmodule</c> (with and without <c>-mt</c>), <c>!name2ee</c> and <c>!token2ee</c>. Each builds a
/// typed model (domains → assemblies → modules; module fields + type tables; EE name/token resolutions)
/// instead of matching raw lines, and the structural parsers fail loudly on a line they don't recognize so
/// a layout change can never be silently dropped. The models let the tests round-trip addresses across
/// commands (a dumpdomain assembly address into dumpassembly, a dumpmodule type-table token into token2ee,
/// etc.) and assert exact equality rather than the legacy scripts' "is it a hex value".
/// </summary>
internal static class ModuleCommandParsing
{
    /// <summary>Run <c>!dumpdomain</c> and parse the full domain/assembly/module tree.</summary>
    public static DumpDomainResult DumpDomain(this Target target) => new(target.Sos("dumpdomain"));

    /// <summary>Run <c>!dumpassembly &lt;addr&gt;</c> for a single assembly.</summary>
    public static DumpAssemblyResult DumpAssembly(this Target target, ulong address) =>
        new(target.Sos($"dumpassembly {address:x}"));

    /// <summary>Run <c>!dumpmodule [-mt] &lt;addr&gt;</c>; pass <paramref name="includeTypes"/> for the
    /// "Types defined / referenced in this module" tables.</summary>
    public static DumpModuleResult DumpModule(this Target target, ulong address, bool includeTypes = false) =>
        new(target.Sos(includeTypes ? $"dumpmodule -mt {address:x}" : $"dumpmodule {address:x}"));

    /// <summary>Run <c>!name2ee &lt;module&gt;!&lt;type-or-method&gt;</c> (may return several matches).</summary>
    public static EEResult Name2EE(this Target target, string moduleQualifiedName) =>
        new(target.Sos($"name2ee {moduleQualifiedName}"));

    /// <summary>Run <c>!token2ee &lt;module&gt; &lt;metadata-token&gt;</c> for one metadata token.</summary>
    public static EEResult Token2EE(this Target target, string module, uint token) =>
        new(target.Sos($"token2ee {module} 0x{token:x}"));
}

/// <summary>Which kind of domain a <c>!dumpdomain</c> block describes.</summary>
public enum DomainKind
{
    /// <summary>The <c>System Domain</c> block (no user assemblies).</summary>
    System,

    /// <summary>The desktop-.NET-Framework-only <c>Shared Domain</c> (holds domain-neutral assemblies such
    /// as mscorlib). Absent on .NET Core.</summary>
    Shared,

    /// <summary>An application domain (<c>Domain N</c>); on .NET Core there is one named "clrhost", on
    /// desktop the default domain is named after the entry-point exe.</summary>
    App,
}

/// <summary>A module reference as printed under an assembly: its address and on-disk path.</summary>
public sealed record ModuleRef(ulong Address, string Path);

/// <summary>An assembly under a domain: its address, path, and the modules it contains.</summary>
public sealed record AssemblyInfo(ulong Address, string Path, IReadOnlyList<ModuleRef> Modules);

/// <summary>One domain block from <c>!dumpdomain</c>.</summary>
public sealed record DomainInfo(
    DomainKind Kind,
    ulong Address,
    string Name,
    ulong LowFrequencyHeap,
    ulong HighFrequencyHeap,
    ulong StubHeap,
    string Stage,
    IReadOnlyList<AssemblyInfo> Assemblies);

/// <summary>The parsed <c>!dumpdomain</c> output: every domain, with helpers for the well-known ones.</summary>
public sealed class DumpDomainResult
{
    private static readonly Regex s_header =
        new(@"^(System Domain|Shared Domain|Domain\s+\d+):\s+([0-9a-fA-F`]+)\s*$", RegexOptions.Compiled);
    private static readonly Regex s_assembly =
        new(@"^Assembly:\s+([0-9a-fA-F`]+)\s+\[(.*)\]\s*$", RegexOptions.Compiled);
    private static readonly Regex s_moduleRow =
        new(@"^\s+([0-9a-fA-F`]+)\s+(.+?)\s*$", RegexOptions.Compiled);
    // A module whose address printed but whose name was pushed onto a later line by interleaved engine
    // noise (see the banner handling in Parse) - i.e. an indented bare hex address with no name.
    private static readonly Regex s_moduleAddrOnly =
        new(@"^\s+([0-9a-fA-F`]+)\s*$", RegexOptions.Compiled);
    private static readonly Regex s_field = new(@"^([A-Za-z][A-Za-z ]+?):\s+(.*?)\s*$", RegexOptions.Compiled);

    public DumpDomainResult(SosOutput output)
    {
        Output = output;
        Domains = Parse(output);
    }

    /// <summary>The raw command output, for custom assertions / rich failures.</summary>
    public SosOutput Output { get; }

    /// <summary>Every domain block in output order.</summary>
    public IReadOnlyList<DomainInfo> Domains { get; }

    /// <summary>The single <c>System Domain</c> block.</summary>
    public DomainInfo SystemDomain =>
        Domains.SingleOrDefault(d => d.Kind == DomainKind.System)
        ?? throw Output.Fail("a System Domain block");

    /// <summary>The desktop-only <c>Shared Domain</c> block, or null on .NET Core.</summary>
    public DomainInfo? SharedDomain => Domains.FirstOrDefault(d => d.Kind == DomainKind.Shared);

    /// <summary>The application domains (<c>Domain N</c>).</summary>
    public IReadOnlyList<DomainInfo> AppDomains => Domains.Where(d => d.Kind == DomainKind.App).ToList();

    /// <summary>Every assembly across every domain.</summary>
    public IEnumerable<AssemblyInfo> AllAssemblies => Domains.SelectMany(d => d.Assemblies);

    /// <summary>The first assembly whose own path, or one of whose module paths, ends with
    /// <paramref name="suffix"/> (case-insensitive), or null. Single-file bundles print an empty assembly
    /// path and carry the name on the module row, so both are checked. Used to find the debuggee's own
    /// assembly regardless of where (or whether) it lives on disk.</summary>
    public AssemblyInfo? FindAssemblyByPathSuffix(string suffix) =>
        AllAssemblies.FirstOrDefault(a =>
            a.Path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
            a.Modules.Any(m => m.Path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)));

    private static List<DomainInfo> Parse(SosOutput output)
    {
        List<DomainInfo> domains = new();

        // Per-domain accumulators.
        DomainKind kind = default;
        ulong address = 0;
        string name = string.Empty, stage = string.Empty;
        ulong low = 0, high = 0, stub = 0;
        List<AssemblyInfo> assemblies = new();
        List<ModuleRef>? currentModules = null;
        ulong currentAssemblyAddr = 0;
        string? currentAssemblyPath = null;
        ulong? pendingModuleAddr = null; // a module address awaiting its name on a later (noise-separated) line
        bool inDomain = false;

        void FlushAssembly()
        {
            if (currentAssemblyPath is not null)
            {
                assemblies.Add(new AssemblyInfo(currentAssemblyAddr, currentAssemblyPath, currentModules ?? new List<ModuleRef>()));
                currentAssemblyPath = null;
                currentModules = null;
            }
        }

        void FlushDomain()
        {
            if (inDomain)
            {
                FlushAssembly();
                domains.Add(new DomainInfo(kind, address, name, low, high, stub, stage, assemblies));
                assemblies = new List<AssemblyInfo>();
                inDomain = false;
            }
        }

        foreach (string raw in output.Lines)
        {
            string line = raw.TrimEnd();
            if (line.Length == 0 || line.StartsWith("---", StringComparison.Ordinal))
            {
                continue;
            }

            // DbgEng can interleave symbol-warning banners onto the NORMAL output channel while resolving
            // the single-file primary module's name - a "*** WARNING:
            // Unable to verify checksum ..." line and, without OS symbols, an "ntdll!PEB ... ***" /
            // "*** ... ***" unqualified-symbol box. These split the module's address from its name. The
            // stripped cdb-sos engine didn't emit them. Drop the banner lines (they start with '*' or, for
            // the box header rows, end with "***"); the address/name split is rejoined via pendingModuleAddr.
            if (line.StartsWith("*", StringComparison.Ordinal) || line.EndsWith("***", StringComparison.Ordinal))
            {
                continue;
            }

            Match header = s_header.Match(line);
            if (header.Success)
            {
                FlushDomain();
                inDomain = true;
                string label = header.Groups[1].Value;
                kind = label.StartsWith("System", StringComparison.Ordinal) ? DomainKind.System
                     : label.StartsWith("Shared", StringComparison.Ordinal) ? DomainKind.Shared
                     : DomainKind.App;
                address = ParseHex(header.Groups[2].Value);
                name = string.Empty;
                stage = string.Empty;
                low = high = stub = 0;
                assemblies = new List<AssemblyInfo>();
                currentModules = null;
                currentAssemblyPath = null;
                continue;
            }

            if (!inDomain)
            {
                throw output.Fail($"a domain header before \"{line}\"");
            }

            Match asm = s_assembly.Match(line);
            if (asm.Success)
            {
                FlushAssembly();
                currentAssemblyAddr = ParseHex(asm.Groups[1].Value);
                currentAssemblyPath = asm.Groups[2].Value.Trim();
                currentModules = new List<ModuleRef>();
                continue;
            }

            if (line.Trim() == "Module")
            {
                continue; // the "  Module" sub-header; the address row(s) follow
            }

            if (currentAssemblyPath is not null && raw.Length > 0 && char.IsWhiteSpace(raw[0]))
            {
                Match mod = s_moduleRow.Match(line);
                if (mod.Success)
                {
                    currentModules!.Add(new ModuleRef(ParseHex(mod.Groups[1].Value), mod.Groups[2].Value.Trim()));
                    continue;
                }

                // Address printed with no name on this line - its name was pushed to a later line by the
                // engine banner noise above; remember the address and pick up the name when it arrives.
                Match addrOnly = s_moduleAddrOnly.Match(line);
                if (addrOnly.Success)
                {
                    pendingModuleAddr = ParseHex(addrOnly.Groups[1].Value);
                    continue;
                }
            }

            // The name line for a module whose address we saw earlier (single-file primary module). It is
            // not indented and matches none of the structural rows above, so resolve it here.
            if (pendingModuleAddr is ulong pendingAddr && currentModules is not null)
            {
                currentModules.Add(new ModuleRef(pendingAddr, line.Trim()));
                pendingModuleAddr = null;
                continue;
            }

            Match field = s_field.Match(line);
            if (field.Success)
            {
                switch (field.Groups[1].Value)
                {
                    case "LowFrequencyHeap": low = ParseHex(field.Groups[2].Value); continue;
                    case "HighFrequencyHeap": high = ParseHex(field.Groups[2].Value); continue;
                    case "StubHeap": stub = ParseHex(field.Groups[2].Value); continue;
                    case "Stage": stage = field.Groups[2].Value.Trim(); continue;
                    case "Name": name = field.Groups[2].Value.Trim(); continue;
                    default: continue; // tolerate extra per-domain heap/diagnostic fields across flavors
                }
            }

            throw output.Fail($"dumpdomain line to be recognized (was \"{line}\")");
        }

        FlushDomain();
        return domains;
    }

    internal static ulong ParseHex(string value) =>
        ulong.Parse(value.Replace("`", string.Empty), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}

/// <summary>The parsed <c>!dumpassembly &lt;addr&gt;</c> output.</summary>
public sealed class DumpAssemblyResult
{
    private static readonly Regex s_moduleRow =
        new(@"^\s+([0-9a-fA-F`]+)\s+(.+?)\s*$", RegexOptions.Compiled);

    public DumpAssemblyResult(SosOutput output)
    {
        Output = output;
        ParentDomain = output["Parent Domain"].AsUInt64(Sos.Addr);
        Name = output["Name"].Value;

        List<ModuleRef> modules = new();
        bool sawModuleHeader = false;
        foreach (string line in output.Lines)
        {
            if (line.Trim() == "Module")
            {
                sawModuleHeader = true;
                continue;
            }

            if (sawModuleHeader && line.Length > 0 && char.IsWhiteSpace(line[0]))
            {
                Match m = s_moduleRow.Match(line.TrimEnd());
                if (m.Success)
                {
                    modules.Add(new ModuleRef(DumpDomainResult.ParseHex(m.Groups[1].Value), m.Groups[2].Value.Trim()));
                }
            }
        }

        Modules = modules;
    }

    public SosOutput Output { get; }
    public ulong ParentDomain { get; }
    public string Name { get; }
    public IReadOnlyList<ModuleRef> Modules { get; }
}

/// <summary>One row of a <c>!dumpmodule -mt</c> type table: the method table, metadata token, and name.</summary>
public readonly record struct TypeEntry(ulong MethodTable, uint Token, string Name);

/// <summary>The parsed <c>!dumpmodule [-mt] &lt;addr&gt;</c> output.</summary>
public sealed class DumpModuleResult
{
    private static readonly Regex s_metadata =
        new(@"^MetaData start address:\s+([0-9a-fA-F`]+)\s+\((\d+)\s+bytes\)", RegexOptions.Compiled);
    private static readonly Regex s_typeRow =
        new(@"^([0-9a-fA-F`]+)\s+0x([0-9a-fA-F]+)\s+(.+?)\s*$", RegexOptions.Compiled);

    public DumpModuleResult(SosOutput output)
    {
        Output = output;
        Name = output["Name"].Value;
        Attributes = output["Attributes"].Value;
        Assembly = output["Assembly"].AsUInt64(Sos.Addr);
        BaseAddress = output["BaseAddress"].AsUInt64(Sos.Addr);
        LoaderHeap = output["LoaderHeap"].AsUInt64(Sos.Addr);
        TypeDefToMethodTableMap = output["TypeDefToMethodTableMap"].AsUInt64(Sos.Addr);
        TypeRefToMethodTableMap = output["TypeRefToMethodTableMap"].AsUInt64(Sos.Addr);
        MethodDefToDescMap = output["MethodDefToDescMap"].AsUInt64(Sos.Addr);
        FieldDefToDescMap = output["FieldDefToDescMap"].AsUInt64(Sos.Addr);
        MemberRefToDescMap = output["MemberRefToDescMap"].AsUInt64(Sos.Addr);

        ulong metaStart = 0;
        long metaSize = 0;
        TypesDefined = new List<TypeEntry>();
        TypesReferenced = new List<TypeEntry>();
        List<TypeEntry>? current = null;

        foreach (string raw in output.Lines)
        {
            string line = raw.TrimEnd();

            Match meta = s_metadata.Match(line);
            if (meta.Success)
            {
                metaStart = DumpDomainResult.ParseHex(meta.Groups[1].Value);
                metaSize = long.Parse(meta.Groups[2].Value, CultureInfo.InvariantCulture);
                continue;
            }

            if (line.StartsWith("Types defined in this module", StringComparison.Ordinal))
            {
                current = (List<TypeEntry>)TypesDefined;
                continue;
            }

            if (line.StartsWith("Types referenced in this module", StringComparison.Ordinal))
            {
                current = (List<TypeEntry>)TypesReferenced;
                continue;
            }

            if (current is null || line.Length == 0 || line.StartsWith("---", StringComparison.Ordinal) ||
                line.TrimStart().StartsWith("MT", StringComparison.Ordinal))
            {
                continue; // header / separator / blank between sections
            }

            Match row = s_typeRow.Match(line);
            if (row.Success)
            {
                current.Add(new TypeEntry(
                    DumpDomainResult.ParseHex(row.Groups[1].Value),
                    uint.Parse(row.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    row.Groups[3].Value.Trim()));
            }
        }

        MetaData = (metaStart, metaSize);
    }

    public SosOutput Output { get; }
    public string Name { get; }
    public string Attributes { get; }
    public ulong Assembly { get; }
    public ulong BaseAddress { get; }
    public ulong LoaderHeap { get; }
    public ulong TypeDefToMethodTableMap { get; }
    public ulong TypeRefToMethodTableMap { get; }
    public ulong MethodDefToDescMap { get; }
    public ulong FieldDefToDescMap { get; }
    public ulong MemberRefToDescMap { get; }

    /// <summary>The metadata blob's start address and size in bytes.</summary>
    public (ulong Start, long Size) MetaData { get; }

    /// <summary>Rows of the "Types defined in this module" table (empty unless <c>-mt</c> was requested).</summary>
    public IReadOnlyList<TypeEntry> TypesDefined { get; }

    /// <summary>Rows of the "Types referenced in this module" table (empty unless <c>-mt</c> was requested).</summary>
    public IReadOnlyList<TypeEntry> TypesReferenced { get; }

    /// <summary>The defined-type row whose name equals <paramref name="name"/>.</summary>
    public TypeEntry DefinedType(string name) =>
        TypesDefined.FirstOrDefault(t => t.Name == name) is { Name: { Length: > 0 } } e
            ? e
            : throw Output.Fail($"a defined type named \"{name}\"");
}

/// <summary>One resolution block from <c>!name2ee</c> / <c>!token2ee</c> (a type or a method).</summary>
public sealed record EEMatch(uint? Token, ulong? MethodTable, ulong? MethodDesc, string Name, ulong? JittedCodeAddress);

/// <summary>The parsed <c>!name2ee</c> / <c>!token2ee</c> output: the shared module/assembly plus one or
/// more matches (name2ee can match several types/methods by prefix).</summary>
public sealed class EEResult
{
    public EEResult(SosOutput output)
    {
        Output = output;

        uint? token = null;
        ulong? methodTable = null, methodDesc = null, jitted = null;
        string? name = null;
        bool started = false;
        List<EEMatch> matches = new();

        void Flush()
        {
            if (started)
            {
                matches.Add(new EEMatch(token, methodTable, methodDesc, name ?? string.Empty, jitted));
                token = null;
                methodTable = methodDesc = jitted = null;
                name = null;
                started = false;
            }
        }

        foreach (string raw in output.Lines)
        {
            string line = raw.TrimEnd();
            if (line.StartsWith("---", StringComparison.Ordinal))
            {
                Flush();
                continue;
            }

            int colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            string key = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();
            switch (key)
            {
                case "Module": Module = DumpDomainResult.ParseHex(value); break;
                case "Assembly": Assembly = value; break;
                case "Token": started = true; token = (uint)DumpDomainResult.ParseHex(value); break;
                case "MethodTable": started = true; methodTable = DumpDomainResult.ParseHex(value); break;
                case "MethodDesc": started = true; methodDesc = DumpDomainResult.ParseHex(value); break;
                case "JITTED Code Address": started = true; jitted = DumpDomainResult.ParseHex(value); break;
                case "Name": started = true; name = value; break;
            }
        }

        Flush();
        Matches = matches;
    }

    public SosOutput Output { get; }
    public ulong Module { get; }
    public string Assembly { get; } = string.Empty;
    public IReadOnlyList<EEMatch> Matches { get; }

    /// <summary>The one and only match; throws if name2ee returned several (use a unique name).</summary>
    public EEMatch Single =>
        Matches.Count == 1 ? Matches[0] : throw Output.Fail($"exactly one EE match (got {Matches.Count})");
}

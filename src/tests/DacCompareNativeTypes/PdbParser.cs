// This "PdbParser" parses the output of the VS sample app `Dia2Dump.exe -t <file>`
//
// It is looking for type definitions and member definitions
//
// Sample lines:
//
// UserDefinedType: __GlobalVal<unsigned int>
// BaseClass      :     Indexer<SString *,SArray<SString *,1>::Iterator>, offset = 0x10
// Data           :   this+0x0, Member, Type: unsigned long *, m_rvaPtr
//
// In this file format the whitspace after the `:` is significant. It indicates nesting.
//   - A type is terminated by eof or the next type definition with the same or lower nesting level.
//   - A nested type and its members are indicated by nesting level (indentation).
//
// We are ignoring other lines types 'Function', 'Enum', 'Typedef' ...

using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using static ParserExtensions;

#nullable enable
class PdbParser
{
    // UserDefinedType: __GlobalVal<unsigned int>
    static Regex typeRegEx = new Regex(@"^UserDefinedType\s*: (?<nesting>\s*)(?<name>.+?)\s*$");

    // Data           :   this+0x0, Member, Type: unsigned long *, m_rvaPtr
    // Data           :   this+0x2D0, Member, Type: class InlineSArray<SString *,16,1>, m_names
    // Data           :   this(bf)+0x0:0x0 len(0x1), Member, Type: bool, m_bGphIsCacheValid
    static Regex memberRegEx = new Regex(@"^Data\s*:   (?<nesting>\s*)this(?:\(bf\))?\+0x(?<offset>[0-9a-fA-F]+)[,:].*Member,\s*Type:\s*(?<type>.+?)\s*,\s*(?<name>[^,]+?)\s*$");

    // BaseClass      :     Indexer<SString *,SArray<SString *,1>::Iterator>, offset = 0x10
    static Regex baseRegEx = new Regex(@"^BaseClass\s*:   (?<nesting>\s*)(?<type>.+?)\s*,\s*offset\s*=\s*0x(?<offset>[0-9a-fA-F]+)\s*$");

    static int bytesPerNest = 2;

    public static IEnumerable<Type> Parse(IEnumerable<string> lines)
    {
        Type? currentType = null;
        int currentNesting = 0;

        foreach (string line in lines)
        {
            Match lineMatch = typeRegEx.Match(line);
            if (lineMatch.Success)
            {
                int nesting = lineMatch.Groups["nesting"].Value.Length/bytesPerNest;

                while (nesting <= currentNesting)
                {
                    if (currentType == null)
                        break;

                    if (currentType.Members.Count > 0)
                        yield return currentType;

                    currentType = currentType.Parent;
                    if (currentNesting > 0)
                        currentNesting -= 1;
                }

                if (nesting > currentNesting + 1)
                    continue;

                currentType = new Type()
                {
                    Name = RemoveWhitespace(lineMatch.Groups["name"].Value),
                    Parent = currentType,
                    SourceLine = line
                };

                currentNesting = nesting;
                continue;
            }

            Match memberMatch = memberRegEx.Match(line);
            if (memberMatch.Success)
            {
                int nesting = memberMatch.Groups["nesting"].Value.Length/bytesPerNest;

                while (nesting < currentNesting)
                {
                    if (currentType == null)
                      throw new System.Exception("Program Error");

                    if (currentType.Members.Count > 0)
                        yield return currentType;

                    currentType = currentType.Parent;
                    currentNesting -= 1;
                }

                if (currentType == null)
                    throw new System.Exception("Program Error");

                if (nesting != currentNesting)
                    continue;

                string typeName = RemoveWhitespace(memberMatch.Groups["name"].Value);

                var member = new Member()
                {
                    Name = typeName,
                    TypeName = typeName,
                    Offset = ulong.Parse(memberMatch.Groups["offset"].Value, NumberStyles.HexNumber),
                    SourceLine = line
                };

                currentType.Members[member.Name] = member;
                continue;
            }

            Match baseMatch = baseRegEx.Match(line);
            if (baseMatch.Success)
            {
                int nesting = baseMatch.Groups["nesting"].Value.Length/bytesPerNest;

                if (currentType == null)
                    throw new System.Exception("Program Error");

                if (nesting != currentNesting)
                    continue;

                string typeName = RemoveTemplateArgs(baseMatch.Groups["type"].Value);
                ulong offset = ulong.Parse(baseMatch.Groups["offset"].Value, NumberStyles.HexNumber);

                var member = new Member()
                {
                    Name = $"({offset:X})",
                    TypeName = typeName,
                    Offset = offset,
                    SourceLine = line
                };

                currentType.Members[member.Name] = member;
                continue;
            }
        }

        if (currentType != null)
            yield return currentType;

        yield break;
    }
}

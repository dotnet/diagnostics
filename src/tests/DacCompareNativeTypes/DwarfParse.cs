// This "DwarfParser" parses the output of `dwarfdump -i -d -G  <file>`
//
// It is looking for type definitions and member definitions
//
// Sample lines:
//
// <1><0x9ad2 GOFF=0x1e17b22><DW_TAG_structure_type> DW_AT_name<"STORAGESIGNATURE"> DW_AT_byte_size<0x00000010> DW_AT_decl_file<0x00000014 /home/stmaclea/git/runtime/src/coreclr/md/inc/mdfileformat.h> DW_AT_decl_line<0x0000003f>
// <1><0x44 GOFF=0x1b51><DW_TAG_structure_type> DW_AT_name<"_GUID"> DW_AT_byte_size<0x00000010> DW_AT_decl_file<0x00000001 /home/stmaclea/git/runtime/src/coreclr/pal/inc/pal_mstypes.h> DW_AT_decl_line<0x000002ab>
// <2><0x4d GOFF=0x1b5a><DW_TAG_member> DW_AT_name<"Data1"> DW_AT_type<<0x00000082 GOFF=0x00001b8f>> DW_AT_decl_file<0x00000001 /home/stmaclea/git/runtime/src/coreclr/pal/inc/pal_mstypes.h> DW_AT_decl_line<0x000002ac> DW_AT_data_member_location<0>

using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using static ParserExtensions;

#nullable enable
class DwarfParser
{
    static Regex typeNameRegEx = new Regex(@"^<(?<nesting>\d+)><.*GOFF=0x(?<goff>[0-9a-fA-F]+)><DW_TAG_((class_type)|(structure_type)|(union_type)|(typedef))>(.*DW_AT_name<(?<name>[^<>]*(((?'Open'<)[^<>]*)+((?'Close-Open'>)[^<>]*)+)*(?(Open)(?!)))>)?");
    // <1><0x9ad2 GOFF=0x1e17b22><DW_TAG_structure_type> DW_AT_name<STORAGESIGNATURE> DW_AT_byte_size<0x00000010> DW_AT_decl_file<0x00000014 /home/stmaclea/git/runtime/src/coreclr/md/inc/mdfileformat.h> DW_AT_decl_line<0x0000003f>
    static Regex typeRegEx = new Regex(@"^<(?<nesting>\d+)><.*GOFF=0x(?<goff>[0-9a-fA-F]+)><DW_TAG_((class)|(structure)|(union))_type>(.*DW_AT_name<(?<name>[^<>]*(((?'Open'<)[^<>]*)+((?'Close-Open'>)[^<>]*)+)*(?(Open)(?!)))>)?.*?DW_AT_decl_file");

    // <2><0x4d GOFF=0x1b5a><DW_TAG_member> DW_AT_name<Data1> DW_AT_type<<0x00000082 GOFF=0x00001b8f>> DW_AT_decl_file<0x00000001 /home/stmaclea/git/runtime/src/coreclr/pal/inc/pal_mstypes.h> DW_AT_decl_line<0x000002ac> DW_AT_data_member_location<0>
    static Regex memberRegEx = new Regex(@"^<(?<nesting>\d+)><.*GOFF=0x(?<goff>[0-9a-fA-F]+)><DW_TAG_member>.*.*?DW_AT_data_member_location<(?<offset>\d+) *([(][^)]*[)])?>");

    // DW_AT_name<Data1>
    static Regex nameRegEx = new Regex(@"DW_AT_name<(?<name>[^<>]*(((?'Open'<)[^<>]*)+((?'Close-Open'>)[^<>]*)+)*(?(Open)(?!)))>");

    // DW_AT_type<<0x00000082 GOFF=0x00001b8f>>
    static Regex typeRefRegEx = new Regex(@"DW_AT_type<<[^>]*GOFF=0x(?<goff>[0-9a-fA-F]+)>>");

    // <2><0x4e1f GOFF=0x88c9><DW_TAG_inheritance> DW_AT_type<<0x00004f87 GOFF=0x00008a31>> DW_AT_data_member_location<0> DW_AT_accessibility<DW_ACCESS_public>
    static Regex baseRegEx = new Regex(@"^<(?<nesting>\d+)><.*GOFF=0x(?<goff>[0-9a-fA-F]+)><DW_TAG_inheritance>.*DW_AT_type<.*GOFF=0x(?<type>[0-9a-fA-F]+)>.*?DW_AT_data_member_location<(?<offset>\d+)>");

    private static void FinishType(Type type)
    {
        if (type.Parent != null)
        {
            Match typeMatch = typeRegEx.Match(type.SourceLine);

            if (typeMatch.Success)
            {
                int goff = int.Parse(typeMatch.Groups["goff"].Value, NumberStyles.HexNumber);

                string name = $"anonymous-member@{goff}";

                Member? anonymousMember = null;
                type.Parent.Members.TryGetValue(name, out anonymousMember);
                if (anonymousMember != null)
                {
                    foreach (Member m in type.Members.Values)
                    {
                        var member = new Member()
                        {
                            Name = m.Name,
                            Offset = anonymousMember.Offset + m.Offset,
                            SourceLine = anonymousMember.SourceLine
                        };

                        type.Parent.Members[member.Name ?? ""] = member;
                    }

                    type.Parent.Members.Remove(name);
                }
            }
            else
            {
                throw new System.Exception("Program Error");
            }
        }
    }

    public static string CleanMemberName(string name)
    {
        name = RemoveWhitespace(name);

        name = name.Replace("_PAL_Undefined", "");

        return name;
    }

    public static IEnumerable<Type> Parse(IEnumerable<string> lines)
    {
        Type? currentType = null;
        int currentNesting = 0;
        Dictionary<int,string> goffNames = new Dictionary<int, string>();

        foreach (string line in lines)
        {
            Match lineMatch = typeNameRegEx.Match(line);
            if (lineMatch.Success)
            {
                int nesting = int.Parse(lineMatch.Groups["nesting"].Value);

                while (nesting <= currentNesting)
                {
                    if (currentType == null)
                        break;

                    currentType = currentType.Parent;
                    if (currentNesting > 0)
                        currentNesting -= 1;
                }

                currentType = new Type()
                {
                    Name = RemoveWhitespace(lineMatch.Groups["name"].Value),
                    Parent = currentType,
                    SourceLine = line
                };

                goffNames[int.Parse(lineMatch.Groups["goff"].Value, NumberStyles.HexNumber)] = currentType?.FullName ?? "";

                currentNesting = nesting;
                continue;
            }
        }

        currentNesting = 0;
        currentType = null;

        foreach (string line in lines)
        {
            Match lineMatch = typeRegEx.Match(line);
            if (lineMatch.Success)
            {
                int nesting = int.Parse(lineMatch.Groups["nesting"].Value);

                while (nesting <= currentNesting)
                {
                    if (currentType == null)
                        break;

                    FinishType(currentType);
                    yield return currentType;

                    currentType = currentType.Parent;
                    if (currentNesting > 0)
                        currentNesting -= 1;
                }

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
                if (line.Contains("DW_AT_artificial<yes(1)>"))
                    continue;

                int nesting = int.Parse(memberMatch.Groups["nesting"].Value) -1;

                while (nesting < currentNesting)
                {
                    if (currentType == null)
                      throw new System.Exception("Program Error");

                    FinishType(currentType);
                    yield return currentType;

                    currentType = currentType.Parent;
                    currentNesting -= 1;
                }

                if (currentType == null)
                    throw new System.Exception("Program Error");

                Match nameMatch = nameRegEx.Match(line);
                Match typeRefMatch = typeRefRegEx.Match(line);
                int goff = -1;
                if (typeRefMatch.Success)
                {
                    goff = int.Parse(typeRefMatch.Groups["goff"].Value, NumberStyles.HexNumber);
                }

                ulong offset = ulong.Parse(memberMatch.Groups["offset"].Value);
                string name;
                if (nameMatch.Success)
                    name = CleanMemberName(nameMatch.Groups["name"].Value);
                else
                    name = $"anonymous-member@{goff}";

                var member = new Member()
                {
                    Name = name,
                    Offset = offset,
                    SourceLine = line
                };

                currentType.Members[member.Name] = member;
                continue;
            }

            Match baseMatch = baseRegEx.Match(line);
            if (baseMatch.Success)
            {
                int nesting = int.Parse(baseMatch.Groups["nesting"].Value) -1;

                if (currentType == null)
                    throw new System.Exception("Program Error");

                if (nesting != currentNesting)
                    continue;

                int goff = int.Parse(baseMatch.Groups["type"].Value, NumberStyles.HexNumber);
                string typeName = RemoveTemplateArgs(goffNames.GetValueOrDefault(goff, $"0x{goff:X}"));
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

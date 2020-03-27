using System.Collections.Generic;
using System.Linq;
using System.Text;

#nullable enable
public class Type
{
    public Type()
    {
        Members = new Dictionary<string, Member>();
        Alternates = new Dictionary<string, Type>();
    }
    public string? Name { get; set; }

    public string? FullName
    {
        get
        {
            if (Parent != null)
              return $"{Parent?.Name}::{Name}";
            return Name;
        }
    }

    public Dictionary<string, Member> Members { get;}

    public Dictionary<string, Type> Alternates { get;}

    public Type? Parent { get; set;}

    public string? SourceLine { get; set; }

    public override string ToString()
    {
        string memberSeparator = "\n  ";
        string members = string.Join(memberSeparator, Members.Values.OrderBy(x => x.Offset).ThenBy(x => x.Name));
        return $@"{FullName}{(Members.Values.Count == 0 ? "" : memberSeparator)}{members}";
    }
}

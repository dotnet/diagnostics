#nullable enable
public class Member
{
    public string? Name { get; set; }

    public string? TypeName { get; set;}

    public ulong? Offset { get; set;}

    public string? SourceLine { get; set; }

    public override string ToString() { return $"{Name} @ 0x{Offset:X}"; }
}

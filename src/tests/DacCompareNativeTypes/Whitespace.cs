using System.Linq;
using System.Text.RegularExpressions;
using static ParserExtensions;

public static class ParserExtensions
{
    static Regex templateRegEx = new Regex(@"^(?<name>[^<]+)\s*<.*>\s*$");
    public static string RemoveWhitespace(string input)
    {
        return new string(input.Where(c => !char.IsWhiteSpace(c)).ToArray());
    }

    public static string RemoveTemplateArgs(string input)
    {
        Match templateMatch = templateRegEx.Match(RemoveWhitespace(input));

        if (templateMatch.Success)
        {
            string name = templateMatch.Groups["name"].Value;
            return $"{name}<.>";
        }
        return input;
    }
}

using System.Text.RegularExpressions;

namespace ActualChat.Media;

public sealed class HostWildcard(string pattern)
{
    private readonly Regex _re = new (GetPattern(pattern), RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public bool IsMatch(string text)
    {
        if (text.IsNullOrEmpty())
            return false;

        return _re.IsMatch(text);
    }

    private static string GetPattern(string s)
        => Regex.Escape(s).Replace(@"\*", @"[^\.]*");
}

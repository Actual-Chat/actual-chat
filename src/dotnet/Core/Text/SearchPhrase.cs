using System.Text.RegularExpressions;

namespace ActualChat;

public sealed class SearchPhrase
{
    private string? _text;
    private Regex? _termRegex;

    public string[] Terms { get; }
    public string Text => _text ??= Terms.ToDelimitedString(" ");
    public bool IsEmpty => Terms.Length == 0;

    public SearchPhrase(string text)
        => Terms = GetTerms(text);
    public SearchPhrase(string[] terms)
        => Terms = terms;

    public override string ToString()
        => $"{GetType().Name}({JsonFormatter.Format(Text)})";

    public Regex GetTermRegex()
    {
        _termRegex ??= new Regex(
            Terms.Select(t => $"({Regex.Escape(t)})").ToDelimitedString("|"),
            RegexOptions.IgnoreCase);
        return _termRegex;
    }

    public SearchMatch GetMatch(string text)
    {
        if (Terms.Length == 0 || text.IsNullOrEmpty())
            return text;

        var matches = GetTermRegex().Matches(text);
        var parts = new SearchMatchPart[matches.Count];
        var rank = 0d;
        for (var i = 0; i < matches.Count; i++) {
            var match = matches[i];
            var index = match.Index;
            var partRank = match.Length / (1d + match.Index);
            parts[i] = new SearchMatchPart((index, index + match.Length), partRank);
            rank += partRank;
        }
        return new SearchMatch(text, rank, parts);
    }

    public double GetMatchRank(string text)
    {
        if (Terms.Length == 0 || text.IsNullOrEmpty())
            return 0;

        var matches = GetTermRegex().Matches(text);
        var rank = 0d;
        foreach (Match match in matches)
            rank += match.Length / (1d + match.Index);
        return rank;
    }

    // Private methods

    private static string[] GetTerms(string text)
        => text.IsNullOrEmpty()
            ? Array.Empty<string>()
            : text.Split().Where(s => !s.IsNullOrEmpty()).ToArray();
}

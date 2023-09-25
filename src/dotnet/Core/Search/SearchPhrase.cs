using System.Text.RegularExpressions;
using Cysharp.Text;
using MemoryPack;

namespace ActualChat.Search;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class SearchPhrase
{
    [GeneratedRegex("[\\s_]+")]
    private static partial Regex TermSplitRegexFactory();
    private static readonly Regex TermSplitRegex = TermSplitRegexFactory();

    public static readonly SearchPhrase None = "".ToSearchPhrase(true, false);

    private string? _text;
    private Regex? _termRegex;

    [DataMember, MemoryPackOrder(0)] public string[] Terms { get; }
    [DataMember, MemoryPackOrder(1)] public bool MatchPrefixes { get; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Text => _text ??= Terms.ToDelimitedString(" ");
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public Regex TermRegex => _termRegex ??= new Regex(GetTermRegexString(), RegexOptions.IgnoreCase);
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsEmpty => Terms.Length == 0;

    public SearchPhrase(string text, bool matchPrefixes, bool matchSuffixes)
    {
        Terms = GetTerms(text, matchSuffixes);
        MatchPrefixes = matchPrefixes;
    }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public SearchPhrase(string[] terms, bool matchPrefixes)
    {
        Terms = terms;
        MatchPrefixes = matchPrefixes;
    }

    public override string ToString()
        => $"{GetType().GetName()}(\"{Text}\", re'{GetTermRegexString()}')";

    public string GetTermRegexString()
    {
        if (IsEmpty)
            return "";
        var sb = ZString.CreateStringBuilder();
        using var _ = sb;
        foreach (var term in Terms) {
            if (term.Length == 0)
                continue;
            if (sb.Length != 0)
                sb.Append("|");
            sb.Append("((^|\\s)?");
            if (MatchPrefixes)
                AddPrefixRegex(term, ref sb);
            else
                RegexHelper.Escape(term, ref sb);
            sb.Append(')');
        }
        return sb.ToString();

        void AddPrefixRegex(ReadOnlySpan<char> word, ref Utf16ValueStringBuilder sb)
        {
            if (word.Length == 0)
                return;
            RegexHelper.Escape(word[0], ref sb);
            var suffix = word[1..];
            if (suffix.Length != 0) {
                sb.Append('(');
                AddPrefixRegex(suffix, ref sb);
                sb.Append(")?");
            }
        }
    }

    public SearchMatch GetMatch(string text)
    {
        if (Terms.Length == 0 || text.IsNullOrEmpty())
            return SearchMatch.New(text);

        var matches = TermRegex.Matches(text);
        var parts = new SearchMatchPart[matches.Count];
        var rank = 0d;
        for (var i = 0; i < matches.Count; i++) {
            var match = matches[i];
            var index = match.Index;
            var length = match.Length + (index == 0 ? 1 : 0);
            var boundaryBoost = index == 0 ? 2 : char.IsWhiteSpace(text[index]) ? 1.5 : 0;
            var partRank = boundaryBoost * Math.Exp(length) * 100 / (100 + index);
            parts[i] = new SearchMatchPart((index, index + match.Length), partRank);
            rank += partRank;
        }
        var result = new SearchMatch(text, rank, parts);
        Debug.WriteLine(result);
        return result;
    }

    public double GetMatchRank(string text)
    {
        if (Terms.Length == 0 || text.IsNullOrEmpty())
            return 0;

        var matches = TermRegex.Matches(text);
        var rank = 0d;
        foreach (Match match in matches) {
            var index = match.Index;
            var length = match.Length + (index == 0 ? 1 : 0);
            var boundaryBoost = index == 0 ? 2 : char.IsWhiteSpace(text[index]) ? 1.5 : 0;
            var partRank = boundaryBoost * Math.Exp(length) * 100 / (100 + index);
            rank += partRank;
        }
        return rank;
    }

    // Private methods

    private static string[] GetTerms(string text, bool addEndings)
    {
        if (text.NullIfWhiteSpace() == null)
            return Array.Empty<string>();

        var parts = TermSplitRegex.Split(text);
        if (!addEndings)
            return parts;

        var result = new List<string>();
        foreach (var part in parts) {
            for (var i = 0; i < part.Length; i++)
                result.Add(part[i..]);
        }
        return result.ToArray();
    }
}

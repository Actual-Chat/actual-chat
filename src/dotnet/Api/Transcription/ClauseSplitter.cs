using System.Text.RegularExpressions;

namespace ActualChat.Transcription;

/// <summary>
/// Cuts text into clauses a translator can take whole and a TTS engine speaks at once:
/// at sentence-ending marks, at comma-class marks once the clause is long enough to be spoken
/// on its own, and at the last space before <see cref="MaxUnpunctuatedLength"/> when a
/// speaker never pauses.
/// </summary>
public static partial class ClauseSplitter
{
    public const int MinCommaClauseLength = 20;
    public const int MaxUnpunctuatedLength = 120;

    // The ASCII marks need a following space or the end: "3.5" or "10:30" end no clause.
    // The fullwidth ones are never followed by a space and never sit inside a number.
    [GeneratedRegex(@"(?<sentence>[.!?…](?=\s|$)|[。！？])|(?<comma>[,;:](?=\s|$)|[，；：])")]
    private static partial Regex ClauseEndRegexFactory();
    private static readonly Regex ClauseEndRegex = ClauseEndRegexFactory();

    public static List<int> Split(string text, int from, bool isEnd)
    {
        var ends = new List<int>();
        var start = from;
        foreach (Match match in ClauseEndRegex.Matches(text, from)) {
            var end = match.Index + match.Length;
            if (match.Groups["comma"].Success && end - start < MinCommaClauseLength)
                continue;

            ends.Add(end);
            start = end;
        }
        while (text.Length - start > MaxUnpunctuatedLength) {
            var limit = start + MaxUnpunctuatedLength;
            var space = text.LastIndexOf(' ', limit, limit - start);
            var end = space > start ? space : limit;
            ends.Add(end);
            start = end;
        }
        if (isEnd && !text.AsSpan(start).IsWhiteSpace())
            ends.Add(text.Length);
        return ends;
    }
}

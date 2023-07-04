using System.Text.RegularExpressions;

namespace ActualChat.Chat;

public sealed partial record PlayableTextMarkup(string Text, LinearMap TimeMap) : TextMarkup(Text)
{
    private const float InfTime = 1e6f;

    [GeneratedRegex("\\S+\\s+")]
    private static partial Regex WordRegexFactory();

    private static readonly Regex WordRegex = WordRegexFactory();

    private ApiArray<Word>? _words;

    public Range<float> TextRange => (0, Text.Length);
    public Range<float> TimeRange => (TimeMap.TryMap(0f) ?? InfTime, TimeMap.TryMap(Text.Length) ?? InfTime);
    public ApiArray<Word> Words => _words ??= GetWords();

    public PlayableTextMarkup() : this("", default) { }

    private ApiArray<Word> GetWords()
    {
        var words = new List<Word>();
        var timeMap = TimeMap;
        for (var start = 0; start < Text.Length;) {
            var match = WordRegex.Match(Text, start);
            if (match.Success) {
                var textRange = new Range<int>(match.Index, match.Index + match.Length);
                var timeRange = (timeMap.TryMap(textRange.Start) ?? InfTime, timeMap.TryMap(textRange.End) ?? InfTime);
                var word = new Word(match.Value, textRange, timeRange);
                words.Add(word);
                start += match.Length;
            }
            else {
                var textRange = new Range<int>(start, Text.Length);
                var timeRange = (timeMap.TryMap(textRange.Start) ?? InfTime, timeMap.TryMap(textRange.End) ?? InfTime);
                var word = new Word(Text.Substring(start), textRange, timeRange);
                words.Add(word);
                break;
            }
        }
        return words.ToApiArray();
    }

    public record struct Word(
        string Value,
        Range<int> TextRange,
        Range<float> TimeRange);
}

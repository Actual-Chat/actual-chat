using System.Text.RegularExpressions;

namespace ActualChat.Chat;

public sealed record PlayableTextMarkup(string Text, LinearMap TextToTimeMap) : PlainTextMarkup(Text)
{
    private const float InfTime = 1e6f;
    private static readonly Regex WordRegex = new("\\S+\\s+", RegexOptions.Compiled);

    private ImmutableArray<Word>? _words;

    public Range<float> TextRange => (0, Text.Length);
    public Range<float> TimeRange => (TextToTimeMap.TryMap(0f) ?? InfTime, TextToTimeMap.TryMap(Text.Length) ?? InfTime);
    public ImmutableArray<Word> Words => _words ??= GetWords();

    public PlayableTextMarkup() : this("", default) { }

    private ImmutableArray<Word> GetWords()
    {
        var words = new List<Word>();
        var ttm = TextToTimeMap;
        for (var start = 0; start < Text.Length;) {
            var match = WordRegex.Match(Text, start);
            if (match.Success) {
                var textRange = new Range<int>(match.Index, match.Index + match.Length);
                var timeRange = (ttm.TryMap(textRange.Start) ?? InfTime, ttm.TryMap(textRange.End) ?? InfTime);
                var word = new Word(match.Value, textRange, timeRange);
                words.Add(word);
                start += match.Length;
            }
            else {
                var textRange = new Range<int>(start, Text.Length);
                var timeRange = (ttm.TryMap(textRange.Start) ?? InfTime, ttm.TryMap(textRange.End) ?? InfTime);
                var word = new Word(Text.Substring(start), textRange, timeRange);
                words.Add(word);
                break;
            }
        }
        return words.ToImmutableArray();
    }

    public record struct Word(
        string Value,
        Range<int> TextRange,
        Range<float> TimeRange);
}

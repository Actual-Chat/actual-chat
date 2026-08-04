using System.Text.RegularExpressions;
using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Text markup with word-to-time mapping for synchronized playback.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public sealed partial class PlayableTextMarkup : TextMarkup
{
    private const float InfTime = 1e6f;
    [GeneratedRegex(@"[\S^\u200B]+[\s\u200B]+")]
    private static partial Regex WordRegexFactory();
    private static readonly Regex WordRegex = WordRegexFactory();

    [DataMember, Key(1)]
    public LinearMap TimeMap { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public Range<float> TextRange => (0, Text.Length);
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public Range<float> TimeRange => (TimeMap.TryMap(0f) ?? InfTime, TimeMap.TryMap(Text.Length) ?? InfTime);
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public Word[] Words => field ??= GetWords();

    // ReSharper disable once ConvertToPrimaryConstructor
    public PlayableTextMarkup(string text, LinearMap timeMap) : base(text)
        => TimeMap = timeMap;

    public override TextMarkup WithText(string text)
        => new PlayableTextMarkup(text, TimeMap);

    // Private methods

    private Word[] GetWords()
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
                start = match.Index + match.Length;
            }
            else {
                var textRange = new Range<int>(start, Text.Length);
                var timeRange = (timeMap.TryMap(textRange.Start) ?? InfTime, timeMap.TryMap(textRange.End) ?? InfTime);
                var word = new Word(Text.Substring(start), textRange, timeRange);
                words.Add(word);
                break;
            }
        }
        return words.ToArray();
    }

    // Nested types

    public record struct Word(
        string Value,
        Range<int> TextRange,
        Range<float> TimeRange);
}

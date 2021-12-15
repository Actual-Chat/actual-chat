using System.Numerics;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Cysharp.Text;
using Stl.Internal;

namespace ActualChat.Transcription;

[DataContract]
public sealed class Transcript
{
    private static readonly Regex StartRegex = new("^\\s*", RegexOptions.Compiled);

    public const float TextToTimeMapTextPrecision = 0.5f;
    public const float TextToTimeMapTimePrecision = 0.1f;
    public static LinearMap EmptyMap { get; } = new(Vector2.Zero, Vector2.Zero);

    [DataMember(Order = 0)]
    public string Text { get; }
    [DataMember(Order = 1)]
    public LinearMap TextToTimeMap { get; }
    [DataMember(Order = 2)]
    public int StableLength { get; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public Range<int> TextRange { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public Range<float> TimeRange { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public int Length => Text.Length;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public int UnstableLength => Length - StableLength;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public float Duration => TimeRange.Size();
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string StableText => Text[..StableLength];
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string UnstableText => Text[StableLength..];

    public Transcript()
        : this("", EmptyMap, 0) { }

    public Transcript(string text, LinearMap textToTimeMap)
        : this(text, textToTimeMap, text.Length) { }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public Transcript(string text, LinearMap textToTimeMap, int stableLength)
    {
        if (stableLength < 0 || stableLength > text.Length)
            throw new ArgumentOutOfRangeException(nameof(stableLength));
        Text = text;
        TextToTimeMap = textToTimeMap;
        var textRange = TextToTimeMap.XRange;
        TextRange = ((int) textRange.Start, (int) textRange.End);
        if (TextRange.Size() != Text.Length)
            throw new ArgumentOutOfRangeException(nameof(textToTimeMap), "TextToTimeMap.Size() != Text.Length");
        TimeRange = TextToTimeMap.YRange;
        StableLength = stableLength;
    }

    public override string ToString()
        => $"'{StableText}'{(UnstableLength != 0 ? $" + ~'{UnstableText}'" : "")} & {TextToTimeMap}";

    public int GetContentStart()
    {
        var match = StartRegex.Match(Text);
        return TextRange.Start + (match.Success ? match.Length : 0);
    }

    public Transcript GetPrefix(int length, float timeOverlap = 0)
        => Split(length, true, false, timeOverlap).Prefix;

    public Transcript GetSuffix(int start, float timeOverlap = 0)
        => Split(start, false, true, timeOverlap).Suffix;

    public (Transcript Prefix, Transcript Suffix) Split(int start, float timeOverlap = 0)
        => Split(start, true, true, timeOverlap);

    public (Transcript Prefix, Transcript Suffix) Split(int start, bool needPrefix, bool needSuffix, float timeOverlap = 0)
    {
        if (start == TextRange.Start)
            return (new Transcript("", EmptyMap.Move(TextToTimeMap[0])), this);
        if (start == TextRange.End)
            return (this, new Transcript("", EmptyMap.Move(TextToTimeMap[0])));

        var map = TextToTimeMap;
        if (start < TextRange.Start)
            throw new ArgumentOutOfRangeException(nameof(start));
        var mapStart = map.Points.IndexOfGreaterOrEqualX(start - 0.1f);
        if (mapStart < 0)
            throw new ArgumentOutOfRangeException(nameof(start));

        var textStart = start - TextRange.Start;
        var timeStart = map.Map(start);
        var overlappingTimeStart = Math.Max(TimeRange.Start, timeStart - timeOverlap);

        Transcript? prefix = null, suffix = null;
        if (needPrefix) {
            var prefixText = Text[..textStart];
            var prefixMap = map[..mapStart]
                .AppendOrUpdateTail(new LinearMap(start, timeStart), TextToTimeMapTextPrecision);
            prefix = new Transcript(prefixText, prefixMap, Math.Min(StableLength, textStart));
        }
        if (needSuffix) {
            var suffixText = Text[textStart..];
            var suffixMap = new LinearMap(start, overlappingTimeStart)
                .AppendOrUpdateTail(map[mapStart..], TextToTimeMapTextPrecision);
            suffix = new Transcript(suffixText, suffixMap, Math.Max(0, StableLength - textStart));
        }
        return (prefix!, suffix!);
    }

    public Transcript DiffWith(Transcript @base)
    {
        var text = Text;
        var map = TextToTimeMap;
        var baseText = @base.Text;
        var baseMap = @base.TextToTimeMap;
        if (Math.Abs(baseMap.YRange.Start - map.YRange.Start) > 1e-6)
            throw new InvalidOperationException("Transcripts should start at the same time.");

        // The logic below is fairly complex, but overall:
        // - We find the max. common part of the map and the text
        // - Take shorter(commonMap, commonText) as our common prefix
        // - Try to augment commonMap with one extra point - (commonTextEnd, itsTime)
        // - Compute the updated map as (commonText, commonMap) + suffix from (updatedText, updatedMap)

        var commonMapPrefixLength = baseMap.Points.CommonPrefixLength(map.Points);
        if (commonMapPrefixLength == 0)
            return this;
        var commonTextPrefixLengthBasedOnMap = (int) baseMap.Points[commonMapPrefixLength - 1].X;

        var commonTextPrefixLength = baseText.AsSpan().CommonPrefixLength(text.AsSpan());
        var lastCommonMapPointIndex = baseMap.Points.IndexOfLowerOrEqualX(commonTextPrefixLength - 0.1f);
        if (lastCommonMapPointIndex < 0)
            return this;
        var commonMapPrefixLengthBasedOnText = lastCommonMapPointIndex + 1;

        commonTextPrefixLength = Math.Min(commonTextPrefixLength, commonTextPrefixLengthBasedOnMap);
        commonMapPrefixLength = Math.Min(commonMapPrefixLength, commonMapPrefixLengthBasedOnText);
        var mapPrefix = baseMap[..commonMapPrefixLength];
        if (mapPrefix.IsEmpty)
            return this;
        var textPrefix = baseText[..commonTextPrefixLength];

        var textSuffix = text[textPrefix.Length..];
        var mapSuffix = map[mapPrefix.Length..];
        mapSuffix = new LinearMap(textPrefix.Length, baseMap.TryMap(textPrefix.Length)!.Value)
            .AppendOrUpdateTail(mapSuffix, TextToTimeMapTextPrecision);

        var diff = new Transcript(textSuffix, mapSuffix);
        diff.AssertValid(); // TODO(AY): Remove this call once we see everything is fine
        return diff;
    }

    public Transcript WithSuffix(string suffix, float suffixEndTime)
    {
        var suffixTextToTimeMap = new LinearMap(
            new Vector2(TextRange.End, TimeRange.End),
            new Vector2(TextRange.End + suffix.Length, suffixEndTime)
        ).TrySimplifyToPoint();
        return WithSuffix(suffix, suffixTextToTimeMap);
    }

    public Transcript WithSuffix(string suffix, LinearMap suffixTextToTimeMap)
    {
        var updatedText = ZString.Concat(Text, suffix);
        var updatedMap = TextToTimeMap.AppendOrUpdateTail(suffixTextToTimeMap, TextToTimeMapTextPrecision);
        return new Transcript(updatedText, updatedMap);
    }

    public Transcript WithDiff(Transcript? diff)
    {
        if (diff == null) return this;

        var diffMap = diff.TextToTimeMap;
        var cutIndex = diff.TextRange.Start - TextRange.Start;
        var retainedText = "";
        if (cutIndex > Text.Length)
            retainedText = Text + new string(' ', cutIndex - Text.Length);
        else if (cutIndex > 0)
            retainedText = Text[..cutIndex];

        var newText = retainedText + diff.Text;
        var newTextToTimeMap = TextToTimeMap.AppendOrUpdateTail(diffMap, TextToTimeMapTextPrecision);
        return new Transcript(newText, newTextToTimeMap);
    }

    public void AssertValid()
    {
        TextToTimeMap.AssertValid();
        if (TextRange.Size() != Text.Length)
            throw Errors.InternalError("TextRange.Size() != Text.Length.");
    }
}

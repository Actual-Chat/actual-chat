using System.Numerics;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Cysharp.Text;
using Stl.Internal;

namespace ActualChat.Transcription;

[DataContract]
public sealed class Transcript
{
    private static readonly Regex StartRegex = new("^\\s+", RegexOptions.Compiled);
    private static readonly Regex EndRegex = new("\\s+$", RegexOptions.Compiled);

    public const float TextToTimeMapTextPrecision = 0.5f;
    public const float TextToTimeMapTimePrecision = 0.1f;
    public static LinearMap EmptyMap { get; } = new(Vector2.Zero, Vector2.Zero);
    public static Transcript Empty { get; } = new();
    public static Transcript EmptyStable { get; } = new("", EmptyMap, true);

    [DataMember(Order = 0)]
    public string Text { get; }
    [DataMember(Order = 1)]
    public LinearMap TextToTimeMap { get; }
    [DataMember(Order = 2)]
    public TranscriptFlags Flags { get; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public Range<int> TextRange { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public Range<float> TimeRange { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public int Length => Text.Length;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public float Duration => TimeRange.Size();
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsStable => 0 != (Flags & TranscriptFlags.Stable);
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsDiff => 0 != (Flags & TranscriptFlags.Diff);

    public Transcript()
        : this("", EmptyMap, default(TranscriptFlags)) { }

    public Transcript(string text, LinearMap textToTimeMap)
        : this(text, textToTimeMap, default(TranscriptFlags)) { }

    public Transcript(string text, LinearMap textToTimeMap, bool isStable)
        : this(text, textToTimeMap, isStable ? TranscriptFlags.Stable : 0) { }

    public Transcript(string text, LinearMap textToTimeMap, bool isStable, bool isDiff)
        : this(text, textToTimeMap, (isStable ? TranscriptFlags.Stable : 0) | (isDiff ? TranscriptFlags.Diff : 0)) { }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public Transcript(string text, LinearMap textToTimeMap, TranscriptFlags flags)
    {
        Text = text;
        TextToTimeMap = textToTimeMap;
        var textRange = TextToTimeMap.XRange;
        TextRange = ((int) textRange.Start, (int) textRange.End);
        if (TextRange.Size() != Text.Length)
            throw new ArgumentOutOfRangeException(nameof(textToTimeMap), "TextToTimeMap.Size() != Text.Length");
        TimeRange = TextToTimeMap.YRange;
        Flags = flags;
    }

    public override string ToString()
        => $"{(IsDiff ? "diff: " : "")}{(IsStable ? "" : "~")}'{Text}' & {TextToTimeMap}";

    public Transcript WithFlags(TranscriptFlags flags)
        => new(Text, TextToTimeMap, flags);

    public int GetContentStart()
    {
        var match = StartRegex.Match(Text);
        return TextRange.Start + (match.Success ? match.Length : 0);
    }

    public int GetContentEnd()
    {
        var match = EndRegex.Match(Text);
        return TextRange.End - (match.Success ? match.Length : 0);
    }

    public float GetContentStartTime()
        => TextToTimeMap.Map(GetContentStart());

    public float GetContentEndTime()
        => TextToTimeMap.Map(Math.Max(TextRange.Start, GetContentEnd() - 0.01f));

    public Transcript GetPrefix(int length, float timeOverlap = 0)
        => Split(length, true, false, timeOverlap).Prefix;

    public Transcript GetSuffix(int start, float timeOverlap = 0)
        => Split(start, false, true, timeOverlap).Suffix;

    public (Transcript Prefix, Transcript Suffix) Split(int start, float timeOverlap = 0)
        => Split(start, true, true, timeOverlap);

    public (Transcript Prefix, Transcript Suffix) Split(int start, bool needPrefix, bool needSuffix, float timeOverlap = 0)
    {
        if (start <= TextRange.Start)
            return (new Transcript("", EmptyMap.Move(TextToTimeMap[0])), this);
        if (start >= TextRange.End)
            return (this, new Transcript("", EmptyMap.Move(TextToTimeMap[^1])));

        var map = TextToTimeMap;
        var mapStart = map.Points.IndexOfGreaterOrEqualX(start - 0.1f);
        if (mapStart < 0)
            throw new InvalidOperationException("Invalid TextToTimeMap (it doesn't contain start).");

        var textStart = start - TextRange.Start;
        var timeStart = map.Map(start);
        var overlappingTimeStart = Math.Max(TimeRange.Start, timeStart - timeOverlap);

        Transcript? prefix = null, suffix = null;
        if (needPrefix) {
            var prefixText = Text[..textStart];
            var prefixMap = map[..mapStart]
                .AppendOrUpdateTail(new LinearMap(start, timeStart), TextToTimeMapTextPrecision);
            prefix = new Transcript(prefixText, prefixMap, Flags);
        }
        if (needSuffix) {
            var suffixText = Text[textStart..];
            var suffixMap = new LinearMap(start, overlappingTimeStart)
                .AppendOrUpdateTail(map[mapStart..], TextToTimeMapTextPrecision);
            suffix = new Transcript(suffixText, suffixMap, Flags);
        }
        return (prefix!, suffix!);
    }

    public Transcript DiffWith(Transcript @base, bool noDiffFlag = false)
    {
        if (IsDiff || @base.IsDiff)
            throw new InvalidOperationException("Can't compute diff for diffs.");
        var text = Text;
        var map = TextToTimeMap;
        var baseText = @base.Text;
        var baseMap = @base.TextToTimeMap;
        var d = map[0] - baseMap[0];
        if (Math.Abs(d.X) > 1e-6 || Math.Abs(d.Y) > 1e-6)
            return this;
        var textRangeStart = TextRange.Start;

        // The logic below is fairly complex, but overall:
        // - We find the max. common part of the map and the text
        // - Take shorter(commonMap, commonText) as our common prefix
        // - Try to augment commonMap with one extra point - (commonTextEnd, itsTime)
        // - Compute the updated map as (commonText, commonMap) + suffix from (updatedText, updatedMap)

        var commonMapPrefixLength = baseMap.Points.CommonPrefixLength(map.Points);
        if (commonMapPrefixLength == 0)
            return this;
        var commonTextPrefixLengthBasedOnMap = (int) baseMap.Points[commonMapPrefixLength - 1].X - textRangeStart;
        var commonTextPrefixLength = baseText.AsSpan(0, commonTextPrefixLengthBasedOnMap)
            .CommonPrefixLength(text.AsSpan(0, commonTextPrefixLengthBasedOnMap));
        commonMapPrefixLength = baseMap.Points.IndexOfLowerOrEqualX(commonTextPrefixLength + textRangeStart);
        if (commonMapPrefixLength <= 0)
            return this;
        // Here commonMapPrefixLength points to a map point that lies before commonTextPrefixLength,
        // and moreover, commonMapPrefixLength lies in the common part of the map too

        var mapPrefix = baseMap[..commonMapPrefixLength];
        if (mapPrefix.IsEmpty)
            return this;
        var textPrefix = baseText[..commonTextPrefixLength];

        var textSuffix = text[textPrefix.Length..];
        var textPrefixRangeEnd = textPrefix.Length + textRangeStart;
        var mapSuffix = new LinearMap(textPrefixRangeEnd, map.Map(textPrefixRangeEnd))
            .AppendOrUpdateTail(map[mapPrefix.Length..], TextToTimeMapTextPrecision);

        var diff = new Transcript(textSuffix, mapSuffix, IsStable, !noDiffFlag);
        diff.AssertValid(); // TODO(AY): Remove this call once we see everything is fine
        return diff;
    }

    public Transcript WithSuffix(string suffix, float suffixEndTime, bool? suffixIsStable = null)
    {
        var suffixTextToTimeMap = new LinearMap(
            new Vector2(TextRange.End, TimeRange.End),
            new Vector2(TextRange.End + suffix.Length, suffixEndTime)
        ).TrySimplifyToPoint();
        return WithSuffix(suffix, suffixTextToTimeMap, suffixIsStable ?? IsStable);
    }

    public Transcript WithSuffix(string suffix, LinearMap suffixTextToTimeMap, bool? suffixIsStable = null)
    {
        var updatedText = ZString.Concat(Text, suffix);
        var updatedMap = TextToTimeMap.AppendOrUpdateTail(suffixTextToTimeMap, TextToTimeMapTextPrecision);
        return new Transcript(updatedText, updatedMap, suffixIsStable ?? IsStable);
    }

    public Transcript WithDiff(Transcript? diff)
    {
        if (diff == null) return this;
        if (!diff.IsDiff)
            return diff;

        var diffMap = diff.TextToTimeMap;
        var cutIndex = diff.TextRange.Start - TextRange.Start;
        var retainedText = "";
        if (cutIndex > Text.Length)
            retainedText = Text + new string(' ', cutIndex - Text.Length);
        else if (cutIndex > 0)
            retainedText = Text[..cutIndex];

        var newText = retainedText + diff.Text;
        var newTextToTimeMap = TextToTimeMap.AppendOrUpdateTail(diffMap, TextToTimeMapTextPrecision);
        return new Transcript(newText, newTextToTimeMap, diff.IsStable);
    }

    public void AssertValid()
    {
        TextToTimeMap.AssertValid();
        if (TextRange.Size() != Text.Length)
            throw Errors.InternalError("TextRange.Size() != Text.Length.");
    }
}

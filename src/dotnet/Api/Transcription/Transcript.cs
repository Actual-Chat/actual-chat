using System.Numerics;
using System.Text.RegularExpressions;

namespace ActualChat.Transcription;

/// <summary>
/// Represents transcribed text with character-to-time mapping.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
[method: MemoryPackConstructor, SerializationConstructor]
public sealed partial record Transcript(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] string Text,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] LinearMap TimeMap,
    [property: DataMember(Order = 2), MemoryPackOrder(2), Key(2)] Language[] Languages
) : ISanitized
{
    [GeneratedRegex(@"^\s*", RegexOptions.Singleline | RegexOptions.ExplicitCapture)]
    private static partial Regex ContentStartRegexFactory();

    [GeneratedRegex(@"\s*$", RegexOptions.Singleline | RegexOptions.ExplicitCapture)]
    private static partial Regex ContentEndRegexFactory();

    public static readonly Regex ContentStartRegex = ContentStartRegexFactory();
    public static readonly Regex ContentEndRegex = ContentEndRegexFactory();

    public static readonly Vector2 TimeMapEpsilon = new(0.1f, 0.1f);
    public static readonly Transcript Empty = New() with { IsStable = true };
    public static readonly Transcript Ellipsis = new("\u2026", new LinearMap(Vector2.Zero, new Vector2(1, 0)), []);
    public static readonly Transcript Unrecognized = new("\u2026\u200B\u2026", new LinearMap(Vector2.Zero, new Vector2(3, 0)), []);

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public int Length => Text.Length;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public Range<int> TextRange => new(0, Text.Length);
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public Range<float> TimeRange => TimeMap.YRange;

    [DataMember(Order = 3), MemoryPackOrder(3), Key(3)]
    public bool IsStable { get; init; }

    public static Transcript New()
        => new ("", LinearMap.Zero, []);

    public override string ToString()
        => $"`{Sanitizer.MaybeSanitize<Sanitizers.PrefixAndLengthHint>(Text)}` + {TimeMap}";

    public bool IsIdenticalTo(Transcript other)
        => Text == other.Text
            && TimeMap.IsIdenticalTo(other.TimeMap, TimeMapEpsilon);

    public Transcript RequireValid()
    {
        TimeMap.RequireValid();
        return this;
    }

    public int GetContentStart()
        => ContentStartRegex.Match(Text).Length;

    public int GetContentEnd()
        => Length - ContentEndRegex.Match(Text).Length;

    public float GetContentStartTime()
        => TimeMap.Map(GetContentStart());

    public float GetContentEndTime()
        => TimeMap.Map(GetContentEnd());

    public Transcript GetPrefix(int length, float? duration = null)
    {
        var vDuration = duration ?? TimeMap.Map(length);
        return new (Text[..length], TimeMap.GetPrefix(vDuration, TimeMapEpsilon.X), Languages);
    }

    public Transcript GetSuffix(int start, float? startDuration = null)
    {
        var vStartDuration = startDuration ?? TimeMap.Map(start);
        return new (Text[start..], TimeMap.GetSuffix(vStartDuration, TimeMapEpsilon.X), Languages);
    }

    public (Transcript Prefix, Transcript Suffix) Split(int length, float? duration = null)
    {
        var vDuration = duration ?? TimeMap.Map(length);
        var maps = TimeMap.Split(vDuration);
        var prefix = new Transcript(Text[..length], maps.Prefix, Languages);
        var suffix = new Transcript(Text[length..], maps.Suffix, Languages);
        return (prefix, suffix);
    }

    public Transcript WithSuffix(string suffix, float? suffixEndTime = null)
        => WithSuffix(suffix, TimeMap, suffixEndTime);

    public Transcript WithSuffix(string suffix, LinearMap timeMap, float? suffixEndTime, Language[]? languages = null)
    {
        if (suffix.IsNullOrEmpty())
            return this;

        var text = Text + suffix;
        if (suffixEndTime is { } vSuffixEndTime)
            timeMap = timeMap.AppendOrUpdateSuffix(new Vector2(text.Length, vSuffixEndTime), TimeMapEpsilon.X);
        return new Transcript(text, timeMap, languages ?? Languages);
    }

    public Transcript WithSuffix(
        string suffix,
        LinearMap suffixTextToTimeMap,
        Language[]? languages = null)
    {
        var text = Text + suffix;
        var timeMap = TimeMap.AppendOrUpdateSuffix(suffixTextToTimeMap, TimeMapEpsilon.X);
        return new Transcript(text, timeMap, languages ?? Languages);
    }

    // Operators

    public static TranscriptDiff operator -(Transcript transcript, Transcript baseTranscript)
        => TranscriptDiff.New(transcript, baseTranscript);
}

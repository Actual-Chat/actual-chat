using System.Numerics;
using System.Text.RegularExpressions;
using MemoryPack;

namespace ActualChat.Transcription;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record Transcript(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] string Text,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] LinearMap TimeMap)
{
    [GeneratedRegex(@"^\s*", RegexOptions.Singleline | RegexOptions.ExplicitCapture)]
    private static partial Regex ContentStartRegexFactory();

    [GeneratedRegex(@"\s*$", RegexOptions.Singleline | RegexOptions.ExplicitCapture)]
    private static partial Regex ContentEndRegexFactory();

    public static readonly Regex ContentStartRegex = ContentStartRegexFactory();
    public static readonly Regex ContentEndRegex = ContentEndRegexFactory();

    public static readonly Vector2 TimeMapEpsilon = new(0.1f, 0.1f);
    public static readonly Transcript Empty = New();

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public int Length => Text.Length;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public Range<int> TextRange => new(0, Text.Length);
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public Range<float> TimeRange => TimeMap.YRange;

    public static Transcript New()
        => new ("", LinearMap.Zero);

    public override string ToString()
        => $"`{Text}` + {TimeMap}";

    public void RequireValid()
        => TimeMap.RequireValid();

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
        return new (Text[..length], TimeMap.GetPrefix(vDuration, TimeMapEpsilon.X));
    }

    public Transcript GetSuffix(int start, float? startDuration = null)
    {
        var vStartDuration = startDuration ?? TimeMap.Map(start);
        return new (Text[start..], TimeMap.GetSuffix(vStartDuration, TimeMapEpsilon.X));
    }

    public (Transcript Prefix, Transcript Suffix) Split(int length, float? duration = null)
    {
        var vDuration = duration ?? TimeMap.Map(length);
        var maps = TimeMap.Split(vDuration);
        var prefix = new Transcript(Text[..length], maps.Prefix);
        var suffix = new Transcript(Text[length..], maps.Suffix);
        return (prefix, suffix);
    }

    public Transcript WithSuffix(string suffix, float? suffixEndTime = null)
        => WithSuffix(suffix, TimeMap, suffixEndTime);
    public Transcript WithSuffix(string suffix, LinearMap timeMap, float? suffixEndTime)
    {
        if (suffix.IsNullOrEmpty())
            return this;

        var text = Text + suffix;
        if (suffixEndTime is { } vSuffixEndTime)
            timeMap = timeMap.TryAppend(new Vector2(text.Length, vSuffixEndTime), TimeMapEpsilon.X);
        return new Transcript(text, timeMap);
    }

    public Transcript WithSuffix(string suffix, LinearMap suffixTextToTimeMap)
    {
        var text = Text + suffix;
        var timeMap = TimeMap.AppendOrUpdateSuffix(suffixTextToTimeMap, TimeMapEpsilon.X);
        return new Transcript(text, timeMap);
    }

    // Operators

    public static TranscriptDiff operator -(Transcript transcript, Transcript baseTranscript)
        => TranscriptDiff.New(transcript, baseTranscript);
}

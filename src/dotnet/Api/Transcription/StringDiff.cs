using Cysharp.Text;

namespace ActualChat.Transcription;

/// <summary>
/// Represents incremental string changes as a start index and suffix text.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public readonly partial record struct StringDiff(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] int Start,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] string? Suffix
) : ICanBeNone<StringDiff>, ISanitized
{
    public static StringDiff None => default;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsNone => ReferenceEquals(Suffix, null);

    public static StringDiff New(string text, string baseText)
    {
        var commonPrefixLength = baseText.GetCommonPrefixLength(text);
        var maxLength = Math.Max(baseText.Length, text.Length);
        return commonPrefixLength == maxLength
            ? None
            : new StringDiff(commonPrefixLength, text[commonPrefixLength..]);
    }

    public override string ToString()
        => IsNone ? "Δ()" : $"Δ({Start}, `{Sanitizer.MaybeSanitize<Sanitizers.PrefixAndLengthHint>(Suffix)}`)";

    public string ApplyTo(string baseText)
    {
        if (IsNone)
            return baseText;

        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        sb.Append(baseText.AsSpan(0, Start));
        sb.Append(Suffix!);
        return sb.ToStringAndRelease();
    }

    // Operators

    public static string operator +(string baseText, StringDiff diff)
        => diff.ApplyTo(baseText);
}

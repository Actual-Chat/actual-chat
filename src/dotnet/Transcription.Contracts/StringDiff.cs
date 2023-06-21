using Cysharp.Text;
using MemoryPack;

namespace ActualChat.Transcription;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public readonly partial record struct StringDiff(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] int Start,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] string? Suffix
    ) : ICanBeNone<StringDiff>
{
    public static StringDiff None { get; } = default;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public bool IsNone => ReferenceEquals(Suffix, null);

    public static StringDiff New(string text, string baseText)
    {
        var commonPrefixLength = baseText.GetCommonPrefixLength(text);
        return commonPrefixLength == text.Length ? None : new StringDiff(commonPrefixLength, text[commonPrefixLength..]);
    }

    public override string ToString()
        => IsNone ? "Δ()" : $"Δ({Start}, `{Suffix}`)";

    public string ApplyTo(string baseText)
    {
        if (IsNone)
            return baseText;

        using var sb = ZString.CreateStringBuilder(true);
        sb.Append(baseText.AsSpan(0, Start));
        sb.Append(Suffix!);
        return sb.ToString();
    }

    // Operators

    public static string operator +(string baseText, StringDiff diff)
        => diff.ApplyTo(baseText);
}

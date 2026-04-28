using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Wire-frozen v2.7 <see cref="ChatNews"/> shape that wraps a <see cref="LegacyChatEntry"/>.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed partial record LegacyChatNews(
    [property: DataMember, MemoryPackOrder(0)] Range<long> TextEntryLidRange,
    [property: DataMember, MemoryPackOrder(1)] LegacyChatEntry? LastTextEntry = null)
{
    public static LegacyChatNews? From(ChatNews? news)
        => news is null
            ? null
            : new LegacyChatNews(
                news.TextEntryLidRange,
                news.LastTextEntry is { } entry ? LegacyChatEntry.From(entry) : null);
}

using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Legacy v2.6 ChatNews with LegacyChatEntry for backward compatibility.
/// Remove once all clients are migrated past v2.6.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
[ParameterComparer(typeof(ByValueParameterComparer))]
[Obsolete("2025.03: Legacy v2.6 compat type")]
public sealed partial record LegacyChatNews(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Range<long> TextEntryIdRange,
    [property: DataMember, MemoryPackOrder(1), Key(1)] LegacyChatEntry? LastTextEntry = null)
{
#pragma warning disable CS0618 // Obsolete
    public static LegacyChatNews? From(ChatNews? news)
        => news == null ? null : new LegacyChatNews(
            news.TextEntryIdRange,
            news.LastTextEntry != null ? LegacyChatEntry.From(news.LastTextEntry) : null);
#pragma warning restore CS0618
}

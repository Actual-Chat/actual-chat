using ActualChat.Kvas;

namespace ActualChat.Users;

/// <summary>
/// Tracks emoji usage frequency to show most-used emojis first.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record UserReactionSettings : IHasKvasKey<UserReactionSettings>
{
    public static string KvasKey => nameof(UserReactionSettings);

    /// <summary>
    /// Maps emoji ID to usage count.
    /// </summary>
    [DataMember, MemoryPackOrder(0)]
    public Dictionary<string, int> EmojiUsageCount { get; init; } = new(StringComparer.Ordinal);

    public UserReactionSettings WithIncrementedUsage(string emojiId)
    {
        var newCount = new Dictionary<string, int>(EmojiUsageCount, StringComparer.Ordinal);
        newCount[emojiId] = newCount.GetValueOrDefault(emojiId) + 1;
        return this with { EmojiUsageCount = newCount };
    }

    public IEnumerable<Emoji> GetSortedEmojis()
    {
        var allEmojis = Emojis.All;
        if (EmojiUsageCount.Count == 0)
            return allEmojis;

        return allEmojis.OrderByDescending(e => EmojiUsageCount.GetValueOrDefault(e.Id.Value));
    }
}

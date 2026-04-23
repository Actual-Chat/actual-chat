using ActualChat.Kvas;

namespace ActualChat.Users;

/// <summary>
/// Tracks emoji ranking to show most-used emojis first.
/// Top 6 emojis have ranks 1-6 (6 = highest/first position).
/// Other emojis have rank 0.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record UserReactionSettings : StoredSettings, IHasKvasKey<UserReactionSettings>
{
    private const int TopCount = 6;

    public static string KvasKey => nameof(UserReactionSettings);

    /// <summary>
    /// Maps emoji ID to rank (1-6 for top emojis, 0 for others).
    /// </summary>
    [DataMember, MemoryPackOrder(0), Key(0)]
    public Dictionary<string, int> EmojiRanks { get; init; } = new();

    public UserReactionSettings WithBubbleUp(string emojiId)
    {
        var newRanks = new Dictionary<string, int>(EmojiRanks);
        var currentRank = newRanks.GetValueOrDefault(emojiId);

        if (currentRank >= TopCount) {
            // Already at top position, nothing to do
            return this;
        }

        var targetRank = currentRank + 1;

        // Find emoji with target rank and swap
        var emojiToSwap = newRanks.FirstOrDefault(kv => kv.Value == targetRank).Key;
        if (emojiToSwap != null) {
            newRanks[emojiToSwap] = currentRank;
        }

        newRanks[emojiId] = targetRank;

        return this with { EmojiRanks = newRanks };
    }

    public IEnumerable<Emoji> GetSortedEmojis()
    {
        var allEmojis = Emojis.All;
        return allEmojis.OrderByDescending(e => GetRank(e.Id.Value));
    }

    private int GetRank(string emojiId)
    {
        if (EmojiRanks.TryGetValue(emojiId, out var rank))
            return rank;

        // Default top 6 emojis
        return emojiId switch {
            "😂" => 6, // Lol
            "😊" => 5, // Smile
            "😘" => 4, // Kiss
            "👍" => 3, // ThumbsUp
            "❤️" => 2, // Love
            "😥" => 1, // Sad
            _ => 0,
        };
    }
}

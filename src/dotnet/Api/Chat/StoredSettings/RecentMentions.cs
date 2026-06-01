using ActualChat.Kvas;

namespace ActualChat.Chat;

[DataContract, MessagePackObject]
public sealed partial record RecentMentions : StoredSettings, IHasOrigin, IHasKvasKey<RecentMentions>
{
    public const int MaxCount = 100;
    public const int MaxUsesPerItem = 5;
    public const double FrequencyBonus = 0.15;
    public static readonly TimeSpan HalfLife = TimeSpan.FromDays(7);

    public static string KvasKey => nameof(RecentMentions);

    [DataMember, Key(0)] public string Origin { get; init; } = "";
    [DataMember, Key(1)] public ApiArray<RecentMention> Items { get; init; } = [];

    public RecentMentions Use(MentionRef id, Moment now)
    {
        var uses = new List<Moment> { now };
        var rest = new List<RecentMention>();
        foreach (var item in Items) {
            if (item.Id == id)
                uses.AddRange(item.Uses);
            else
                rest.Add(item);
        }
        if (uses.Count > MaxUsesPerItem)
            uses.RemoveRange(MaxUsesPerItem, uses.Count - MaxUsesPerItem);

        var items = new List<RecentMention>(rest.Count + 1) { new() { Id = id, Uses = new ApiArray<Moment>(uses) } };
        items.AddRange(rest);
        if (items.Count > MaxCount) {
            items.Sort((a, b) => ComputeScore(b, now).CompareTo(ComputeScore(a, now)));
            items.RemoveRange(MaxCount, items.Count - MaxCount);
        }
        return this with { Items = new ApiArray<RecentMention>(items) };
    }

    // Maps each remembered mention to its recency+frequency score (higher = more relevant).
    public Dictionary<MentionRef, double> GetScores(Moment now)
    {
        var result = new Dictionary<MentionRef, double>(Items.Count);
        foreach (var item in Items)
            result[item.Id] = ComputeScore(item, now);
        return result;
    }

    // Recency core (most recent use, half-life decayed) lifted by a small per-extra-use frequency bonus.
    // Bounded to ~[0, 1.6] so it stays comparable to the [0, 1] search-coverage score.
    public static double ComputeScore(RecentMention item, Moment now)
    {
        var uses = item.Uses;
        if (uses.Count == 0)
            return 0;

        var halfLifeDays = HalfLife.TotalDays;
        var bestDecay = 0d;
        foreach (var use in uses) {
            var ageDays = Math.Max(0, (now - use).TotalDays);
            var decay = Math.Pow(0.5, ageDays / halfLifeDays);
            if (decay > bestDecay)
                bestDecay = decay;
        }
        var frequency = 1 + FrequencyBonus * Math.Min(MaxUsesPerItem - 1, uses.Count - 1);
        return bestDecay * frequency;
    }
}

[DataContract, MessagePackObject]
public sealed partial record RecentMention
{
    [DataMember, Key(0)] public MentionRef Id { get; init; } = null!;
    [DataMember, Key(1)] public ApiArray<Moment> Uses { get; init; } = []; // Newest first
}

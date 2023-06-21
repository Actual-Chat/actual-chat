using ActualChat.Kvas;
using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record UserBubblesSettings : IHasOrigin
{
    public const string KvasKey = nameof(UserBubblesSettings);

    [DataMember, MemoryPackOrder(0)] public ImmutableArray<string> ReadBubbles { get; init; } = ImmutableArray<string>.Empty;
    [DataMember, MemoryPackOrder(1)] public string Origin { get; init; } = "";

    public UserBubblesSettings WithReadBubbles(params string[] bubbleRefs)
    {
        if (!bubbleRefs.Any())
            return this;

        var readBubbles = bubbleRefs.Aggregate(
            ReadBubbles,
            (bubbles, bubble) => bubbles.Contains(bubble, StringComparer.Ordinal)
                ? bubbles
                : bubbles.Add(bubble));

        return this with { ReadBubbles = readBubbles };
    }
}

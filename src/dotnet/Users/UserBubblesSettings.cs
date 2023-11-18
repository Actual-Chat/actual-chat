using ActualChat.Kvas;
using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record UserBubbleSettings : IHasOrigin
{
    public const string KvasKey = nameof(UserBubbleSettings);

    [DataMember, MemoryPackOrder(0)] public ApiArray<string> ReadBubbles { get; init; }
    [DataMember, MemoryPackOrder(1)] public string Origin { get; init; } = "";

    public UserBubbleSettings WithReadBubbles(params string[] bubbleRefs)
    {
        if (bubbleRefs.Length == 0)
            return this;

        var readBubbles = bubbleRefs.Aggregate(
            ReadBubbles,
            (bubbles, bubble) => bubbles.Contains(bubble, StringComparer.Ordinal)
                ? bubbles
                : bubbles.Add(bubble));

        return this with { ReadBubbles = readBubbles };
    }
}

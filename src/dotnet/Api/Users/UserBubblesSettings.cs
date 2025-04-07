using ActualChat.Kvas;
using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record UserBubbleSettings : IHasOrigin
{
    public const string KvasKey = nameof(UserBubbleSettings);

    [DataMember, MemoryPackOrder(0)] private ApiArray<string> LegacyReadBubbles { get; init; }

    [IgnoreDataMember, MemoryPackIgnore]
    public string[] ReadBubbles {
        get => LegacyReadBubbles.Items;
        init => LegacyReadBubbles = value.ToApiArray();
    }

    [DataMember, MemoryPackOrder(1)] public string Origin { get; init; } = "";

    public UserBubbleSettings WithRead(params string[] bubbleRefs)
    {
        if (bubbleRefs.Length == 0)
            return this;

        var readBubbles = bubbleRefs.Aggregate(
            ReadBubbles,
            (bubbles, bubble) => bubbles.Contains(bubble, StringComparer.Ordinal)
                ? bubbles
                : bubbles.With(bubble));

        return this with { ReadBubbles = readBubbles };
    }

    public UserBubbleSettings WithAllUnread()
        => this with { ReadBubbles = [] };
}

using ActualChat.Kvas;
using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record UserBubbleSettings : IHasOrigin
{
    public const string KvasKey = nameof(UserBubbleSettings);
    private readonly string[] _readBubbles = [];

    [DataMember, MemoryPackOrder(0)]
    private ApiArray<Symbol> LegacyReadBubbles {
        get => _readBubbles.Select(x => new Symbol(x)).ToApiArray();
        init => _readBubbles = value.Select(x => x.Value).ToArray(value.Count);
    }

    [IgnoreDataMember, MemoryPackIgnore]
    public string[] ReadBubbles {
        get => _readBubbles;
        init => _readBubbles = value;
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

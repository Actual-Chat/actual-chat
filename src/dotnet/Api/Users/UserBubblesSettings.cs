using ActualChat.Kvas;
using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record UserBubbleSettings : IHasOrigin
{
    public const string KvasKey = nameof(UserBubbleSettings);

    [DataMember, MemoryPackOrder(0), MemoryPackInclude]
    private ApiArray<Symbol> LegacyReadBubbles {
        get => ReadBubbles.ToApiArray();
        init => ReadBubbles = value.ToList();
    }

    [IgnoreDataMember, MemoryPackIgnore]
    public IReadOnlyList<Symbol> ReadBubbles { get; init; } = [];

    [DataMember, MemoryPackOrder(1)] public string Origin { get; init; } = "";

    public UserBubbleSettings WithRead(params string[] bubbleRefs)
    {
        if (bubbleRefs.Length == 0)
            return this;

        var newReadBubbles = ReadBubbles
            .Concat(bubbleRefs.Select(x => (Symbol)x))
            .Distinct()
            .ToList();
        return this with { ReadBubbles = newReadBubbles };
    }

    public UserBubbleSettings WithAllUnread()
        => this with { ReadBubbles = [] };
}

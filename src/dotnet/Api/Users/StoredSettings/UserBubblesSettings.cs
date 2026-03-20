using ActualChat.Kvas;

namespace ActualChat.Users;

/// <summary>
/// Tracks which help bubbles the user has read.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record UserBubbleSettings : StoredSettings, IHasOrigin
{
    public const string KvasKey = nameof(UserBubbleSettings);

    [DataMember, MemoryPackOrder(0), MemoryPackInclude]
    private ApiArray<Symbol> LegacyReadBubbles {
        get => ReadBubbles.ToApiArray();
        init => ReadBubbles = value.ToList();
    }

    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
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

    public UserBubbleSettings WithoutRead(string bubbleRef)
    {
        var newReadBubbles = ReadBubbles.Where(x => x.Value != bubbleRef).ToList();
        return newReadBubbles.Count == ReadBubbles.Count ? this : this with { ReadBubbles = newReadBubbles };
    }

    public UserBubbleSettings WithAllUnread()
        => this with { ReadBubbles = [] };
}

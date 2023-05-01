using ActualChat.Kvas;

namespace ActualChat.Users;

[DataContract]
public sealed record UserBubblesSettings : IHasOrigin
{
    public const string KvasKey = nameof(UserBubblesSettings);

    [DataMember] public bool Skipped { get; init; }
    [DataMember] public ImmutableArray<string> ReadBubbles { get; init; } = ImmutableArray<string>.Empty;
    [DataMember] public string Origin { get; init; } = "";

    public UserBubblesSettings WithReadBubble(string bubbleRef)
    {
        if (ReadBubbles.Contains(bubbleRef, StringComparer.Ordinal))
            return this;

        var bubbles = ReadBubbles.Add(bubbleRef);
        return this with { ReadBubbles = bubbles };
    }
}

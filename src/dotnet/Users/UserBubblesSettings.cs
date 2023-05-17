using ActualChat.Kvas;

namespace ActualChat.Users;

[DataContract]
public sealed record UserBubblesSettings : IHasOrigin
{
    public const string KvasKey = nameof(UserBubblesSettings);

    [DataMember] public ImmutableArray<string> ReadBubbles { get; init; } = ImmutableArray<string>.Empty;
    [DataMember] public string Origin { get; init; } = "";

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

namespace ActualChat.Live;

[DataContract, MessagePackObject]
public sealed partial record CallReaction(
    [property: DataMember(Order = 0), Key(0)] AuthorId AuthorId,
    [property: DataMember(Order = 1), Key(1)] Emoji Emoji,
    [property: DataMember(Order = 2), Key(2)] Moment SentAt)
{
    public static readonly Emoji[] AllowedEmojis = [
        Emojis.ThumbsUp,
        Emojis.Love,
        Emojis.Lol,
        Emojis.Surprise,
        Emojis.Party,
        Emojis.Fire,
    ];
}

namespace ActualChat.Chat;

[DataContract]
public sealed record ChatNews(
    [property: DataMember] Range<long> TextEntryIdRange,
    [property: DataMember] ChatEntry? LastTextEntry = null
    ) : IRequirementTarget
{
    public static ChatNews None { get; } = new(default(Range<long>));
}

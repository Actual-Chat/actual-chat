namespace ActualChat.Chat;

[DataContract]
public sealed record ChatSummary(
    [property: DataMember] ChatId ChatId,
    [property: DataMember] Range<long> TextEntryIdRange,
    [property: DataMember] ChatEntry? LastTextEntry = null
    ) : IRequirementTarget
{
    public static ChatSummary None { get; } = new(default, default);
}

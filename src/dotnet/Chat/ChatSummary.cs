namespace ActualChat.Chat;

[DataContract]
public sealed record ChatSummary : IRequirementTarget
{
    [DataMember] public Range<long> TextEntryIdRange { get; init; }
    [DataMember] public ChatEntry? LastTextEntry { get; init; }
}

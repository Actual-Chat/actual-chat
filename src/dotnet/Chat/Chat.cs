#pragma warning disable MA0049 // Allows ActualChat.Chat.Chat

namespace ActualChat.Chat;

[DataContract]
public sealed record Chat : IRequirementTarget
{
    [DataMember] public Symbol Id { get; init; } = "";
    [DataMember] public long Version { get; init; }
    [DataMember] public string Title { get; init; } = "";
    [DataMember] public Moment CreatedAt { get; init; }
    [DataMember] public ChatType ChatType { get; init; } = ChatType.Group;
    [DataMember] public bool IsPublic { get; init; }
    [DataMember] public string Picture { get; init; } = "";
}

[DataContract]
public sealed record ChatDiff : RecordDiff
{
    [DataMember] public string? Title { get; init; }
    [DataMember] public ChatType? ChatType { get; init; }
    [DataMember] public bool? IsPublic { get; init; }
    [DataMember] public string? Picture { get; init; }
}

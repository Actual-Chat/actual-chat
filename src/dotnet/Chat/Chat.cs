#pragma warning disable MA0049 // Allows ActualChat.Chat.Chat

namespace ActualChat.Chat;

[DataContract]
public sealed record Chat : IHasId<ChatId>, IRequirementTarget
{
    public static Requirement<Chat> MustExist { get; } = Requirement.New(
        new(() => StandardError.Chat.Unavailable()),
        (Chat? c) => c is { Id.IsEmpty: false });

    [DataMember] public ChatId Id { get; init; }
    [DataMember] public long Version { get; init; }
    [DataMember] public string Title { get; init; } = "";
    [DataMember] public Moment CreatedAt { get; init; }
    [DataMember] public bool IsPublic { get; init; }
    [DataMember] public string Picture { get; init; } = "";

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatKind Kind => Id.Kind;
}

[DataContract]
public sealed record ChatDiff : RecordDiff
{
    [DataMember] public string? Title { get; init; }
    [DataMember] public ChatKind? Kind { get; init; }
    [DataMember] public bool? IsPublic { get; init; }
    [DataMember] public string? Picture { get; init; }
}

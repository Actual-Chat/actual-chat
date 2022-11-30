using Stl.Versioning;

#pragma warning disable MA0049 // Allows ActualChat.Chat.Chat

namespace ActualChat.Chat;

[DataContract]
public sealed record Chat(
    [property: DataMember] ChatId Id,
    [property: DataMember] long Version = 0
    ) : IHasId<ChatId>, IHasVersion<long>, IRequirementTarget
{
    public static Requirement<Chat> MustExist { get; } = Requirement.New(
        new(() => StandardError.Chat.Unavailable()),
        (Chat? c) => c is { Id.IsNone: false });

    [DataMember] public string Title { get; init; } = "";
    [DataMember] public Moment CreatedAt { get; init; }
    [DataMember] public bool IsPublic { get; init; }
    [DataMember] public string Picture { get; init; } = "";

    // Populated only on front-end
    [DataMember] public AuthorRules Rules { get; init; } = null!;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatKind Kind => Id.Kind;
}

[DataContract]
public sealed record ChatDiff : RecordDiff
{
    [DataMember] public string? Title { get; init; }
    [DataMember] public bool? IsPublic { get; init; }
    [DataMember] public string? Picture { get; init; }
}

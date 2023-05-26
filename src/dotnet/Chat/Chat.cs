using ActualChat.Comparison;
using Stl.Fusion.Blazor;
using Stl.Versioning;

#pragma warning disable MA0049 // Allows ActualChat.Chat.Chat

namespace ActualChat.Chat;

[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract]
public sealed record Chat(
    [property: DataMember] ChatId Id,
    [property: DataMember] long Version = 0
    ) : IHasId<ChatId>, IHasVersion<long>, IRequirementTarget
{
    public static IdAndVersionEqualityComparer<Chat, ChatId> EqualityComparer { get; } = new();

    public static Requirement<Chat> MustExist { get; } = Requirement.New(
        new(() => StandardError.NotFound<Chat>()),
        (Chat? c) => c is { Id.IsNone: false });

    public static Requirement<Chat> MustBeTemplate { get; } = MustExist
        & Requirement.New<Chat>(
            new (() => StandardError.Chat.NonTemplate()),
            c => c is { IsPublic: true, IsTemplate: true });

    [DataMember] public string Title { get; init; } = "";
    [DataMember] public Moment CreatedAt { get; init; }
    [DataMember] public bool IsPublic { get; init; }
    [DataMember] public bool IsTemplate { get; init; }
    [DataMember] public ChatId? TemplateId { get; init; }
    [DataMember] public UserId? TemplatedForUserId { get; init; }
    [Obsolete("Kept only for api compatibility with mobile apps", true)]
    [DataMember] public ChatAuthorKind AllowedAuthorKind { get; init; }
    [DataMember] public bool AllowGuestAuthors { get; init; }
    [DataMember] public bool AllowAnonymousAuthors { get; init; }
    [DataMember] public MediaId MediaId { get; init; }

    // Populated only on front-end
    [DataMember] public AuthorRules Rules { get; init; } = null!;
    [DataMember] public Media.Media? Picture { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatKind Kind => Id.Kind;

    public Chat() : this(ChatId.None) { }

    // This record relies on version-based equality
    public bool Equals(Chat? other) => EqualityComparer.Equals(this, other);
    public override int GetHashCode() => EqualityComparer.GetHashCode(this);
}

[DataContract]
public sealed record ChatDiff : RecordDiff
{
    [DataMember] public string? Title { get; init; }
    [DataMember] public ChatKind? Kind { get; init; }
    [DataMember] public bool? IsPublic { get; init; }
    [DataMember] public bool? IsTemplate { get; init; }
    [DataMember] public Option<ChatId?> TemplateId { get; init; }
    [DataMember] public Option<UserId?> TemplatedForUserId { get; init; }
    [Obsolete("Kept only for api compatibility with mobile apps", true)]
    [DataMember] public ChatAuthorKind? AllowedAuthorKind { get; init; }
    [DataMember] public bool? AllowGuestAuthors { get; init; }
    [DataMember] public bool? AllowAnonymousAuthors { get; init; }
    [DataMember] public MediaId? MediaId { get; init; }
}

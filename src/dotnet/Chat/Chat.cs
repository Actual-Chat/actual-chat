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

    public static Chat None { get; } = new(default, 0) {
        Title = "This chat is unavailable",
        Rules = AuthorRules.None(default),
    };
    public static Chat Loading { get; } = new(default, -1) {
        Title = "Loading...",
        Rules = AuthorRules.None(default),
    };

    public static Requirement<Chat> MustExist { get; } = Requirement.New(
        new(() => StandardError.NotFound<Chat>()),
        (Chat? c) => c is { Id.IsNone: false });

    [DataMember] public string Title { get; init; } = "";
    [DataMember] public Moment CreatedAt { get; init; }
    [DataMember] public bool IsPublic { get; init; }
    [DataMember] public string Picture { get; init; } = "";

    // Populated only on front-end
    [DataMember] public AuthorRules Rules { get; init; } = null!;

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
    [DataMember] public string? Picture { get; init; }
}

using MemoryPack;
using Stl.Fusion.Blazor;
using Stl.Versioning;

#pragma warning disable MA0049 // Allows ActualChat.Chat.Chat

namespace ActualChat.Chat;

[ParameterComparer(typeof(ByIdAndVersionParameterComparer<ChatId, long>))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record Chat(
    [property: DataMember, MemoryPackOrder(0)] ChatId Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0
    ) : IHasId<ChatId>, IHasVersion<long>, IRequirementTarget
{
    public static Requirement<Chat> MustExist { get; } = Requirement.New(
        new(() => StandardError.NotFound<Chat>()),
        (Chat? c) => c is { Id.IsNone: false });

    public static Requirement<Chat> MustBeTemplate { get; } = MustExist
        & Requirement.New<Chat>(
            new (() => StandardError.Chat.NonTemplate()),
            c => c is { IsPublic: true, IsTemplate: true });

    [DataMember, MemoryPackOrder(2)] public string Title { get; init; } = "";
    [DataMember, MemoryPackOrder(3)] public Moment CreatedAt { get; init; }
    [DataMember, MemoryPackOrder(4)] public bool IsPublic { get; init; }
    [DataMember, MemoryPackOrder(5)] public bool IsTemplate { get; init; }
    [DataMember, MemoryPackOrder(6)] public ChatId? TemplateId { get; init; }
    [DataMember, MemoryPackOrder(7)] public UserId? TemplatedForUserId { get; init; }
    [DataMember, MemoryPackOrder(8)] public bool AllowGuestAuthors { get; init; }
    [DataMember, MemoryPackOrder(9)] public bool AllowAnonymousAuthors { get; init; }
    [DataMember, MemoryPackOrder(10)] public MediaId MediaId { get; init; }

    // Populated only on front-end
    [DataMember, MemoryPackOrder(11)] public AuthorRules Rules { get; init; } = null!;
    [DataMember, MemoryPackOrder(12)] public Media.Media? Picture { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public ChatKind Kind => Id.Kind;

    // This record relies on referential equality
    public bool Equals(Chat? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record ChatDiff : RecordDiff
{
    [DataMember, MemoryPackOrder(0)] public string? Title { get; init; }
    [DataMember, MemoryPackOrder(1)] public ChatKind? Kind { get; init; }
    [DataMember, MemoryPackOrder(2)] public bool? IsPublic { get; init; }
    [DataMember, MemoryPackOrder(3)] public bool? IsTemplate { get; init; }
    [DataMember, MemoryPackOrder(4)] public Option<ChatId?> TemplateId { get; init; }
    [DataMember, MemoryPackOrder(5)] public Option<UserId?> TemplatedForUserId { get; init; }
    [DataMember, MemoryPackOrder(6)] public bool? AllowGuestAuthors { get; init; }
    [DataMember, MemoryPackOrder(7)] public bool? AllowAnonymousAuthors { get; init; }
    [DataMember, MemoryPackOrder(8)] public MediaId? MediaId { get; init; }
}

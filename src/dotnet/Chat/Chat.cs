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
    public static readonly Requirement<Chat> MustExist = Requirement.New(
        new(() => StandardError.NotFound<Chat>()),
        (Chat? c) => c is { Id.IsNone: false });

    public static readonly Requirement<Chat> MustBeTemplate = MustExist
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
    [DataMember, MemoryPackOrder(14)] public Symbol SystemTag { get; init; }

    // Populated only on front-end
    [DataMember, MemoryPackOrder(11)] public AuthorRules Rules { get; init; } = null!;
    [DataMember, MemoryPackOrder(12)] public Media.Media? Picture { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ChatKind Kind => Id.Kind;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool HasSingleAuthor => SystemTag == Constants.Chat.SystemTags.Notes;

    public bool CanInvite()
        // Technically it should be:
        // => Rules.CanInvite() && !HasSingleAuthor && !Id.IsPeerChat(out _) &&;
        // But since we can't manage other roles than Owner yet,
        // we let only Owners to invite people to chat.
        => Rules.IsOwner() && !HasSingleAuthor && !Id.IsPeerChat(out _);

    public bool IsPublicPlaceChat()
        => Kind == ChatKind.Place && !Id.PlaceChatId.IsRoot && IsPublic;

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
    [DataMember, MemoryPackOrder(10)] public Symbol? SystemTag { get; init; }
    [DataMember, MemoryPackOrder(11)] public PlaceId? PlaceId { get; init; }
}

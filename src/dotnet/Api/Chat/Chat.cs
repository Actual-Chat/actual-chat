using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

#pragma warning disable MA0049 // Allows ActualChat.Chat.Chat

namespace ActualChat.Chat;

[DataContract, MessagePackObject]
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed partial record Chat(
    [property: DataMember, Key(0)] ChatId Id,
    [property: DataMember, Key(1)] long Version = 0
    ) : IHasId<ChatId>, IHasVersion<long>, IRequirementTarget
{
    public static readonly Requirement<Chat> MustExist = Requirement.New(
        (Chat? c) => c?.Id is not null,
        new(() => StandardError.NotFound<Chat>()));

    public static readonly Requirement<Chat> MustBeTemplate = MustExist
        & Requirement.New<Chat>(
            c => c is { IsPublic: true, IsTemplate: true },
            new(() => StandardError.Chat.NonTemplate()));

    public static readonly Requirement<Chat> MustBePlaceRoot = MustExist
        & Requirement.New<Chat>(
            c => c?.Id is PlaceChatId { IsRoot: true },
            new(() => StandardError.Constraint<Chat>("Place root chat is expected.")));

    [DataMember, Key(2)] public string Title { get; init; } = "";
    [DataMember, Key(3)] public Moment CreatedAt { get; init; }
    [DataMember, Key(4)] public bool IsPublic { get; init; }
    [DataMember, Key(5)] public bool IsTemplate { get; init; }
    [DataMember, Key(6)] public ChatId? TemplateId { get; init; }
    [DataMember, Key(7)] public UserId? TemplatedForUserId { get; init; }
    [DataMember, Key(8)] public bool AllowGuestAuthors { get; init; }
    [DataMember, Key(9)] public bool AllowAnonymousAuthors { get; init; }
    [DataMember, Key(10)] public MediaId? MediaId { get; init; }
    [DataMember, Key(13)] public Symbol SystemTag { get; init; }
    [DataMember, Key(14)] public bool IsArchived { get; init; }
    [DataMember, Key(15)] public string Description { get; init; } = "";
    [DataMember, Key(16)] public AliasId? AliasId { get; init; }
    [DataMember, Key(17)] public bool? IsSummarized { get; init; }

    // Populated only on front-end
    [DataMember, Key(11)] public AuthorRules Rules { get; init; } = null!;
    [DataMember, Key(12)] public Media.Media? Picture { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ChatKind Kind => Id.Kind;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool HasSingleAuthor => SystemTag == Constants.Chat.SystemTags.Notes;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public AliasInfo<ChatId> AliasInfo => field ??= new(Id, AliasId);

    public bool CanInvite()
        // Technically it should be:
        // => Rules.CanInvite() && !HasSingleAuthor && !Id.IsPeerChat(out _) &&;
        // But since we can't manage other roles than Owner yet,
        // we let only Owners to invite people to chat.
        => Rules.IsOwner() && Rules.CanInvite() && !HasSingleAuthor && Id is not PeerChatId;

    public bool IsPublicPlaceChat()
        => IsPublic && Id is PlaceChatId { IsRoot: false };

    // This record relies on referential equality
    public bool Equals(Chat? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

[DataContract, MessagePackObject(true)]
public sealed partial record ChatDiff : RecordDiff
{
    [DataMember] public string? Title { get; init; }
    [DataMember] public ChatKind? Kind { get; init; }
    [DataMember] public bool? IsPublic { get; init; }
    [DataMember] public bool? IsTemplate { get; init; }
    [DataMember] public Option<ChatId?> TemplateId { get; init; }
    [DataMember] public Option<UserId?> TemplatedForUserId { get; init; }
    [DataMember] public bool? AllowGuestAuthors { get; init; }
    [DataMember] public bool? AllowAnonymousAuthors { get; init; }
    [DataMember] public MediaId? MediaId { get; init; }
    [DataMember] public Symbol? SystemTag { get; init; }
    [DataMember] public PlaceId? PlaceId { get; init; }
    [DataMember] public bool? IsArchived { get; init; }
    [DataMember] public string? Description { get; init; }
    [DataMember] public AliasId? AliasId { get; init; }
    [DataMember] public Option<bool?> IsSummarized { get; init; }
}

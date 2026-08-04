using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Contacts;

/// <summary>
/// Represents a user's contact entry (chat, user, or place).
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public sealed partial record Contact(
    [property: DataMember, Key(0)] ContactId Id,
    [property: DataMember, Key(1)] long Version = 0
    ) : IHasId<ContactId>, IHasVersion<long>, IRequirementTarget
{
    public static readonly Requirement<Contact> MustExist = Requirement.New(
        (Contact? c) => c?.Id is not null,
        new(() => StandardError.NotFound<Contact>()));

    [DataMember, Key(2)] public UserId? UserId { get; init; }
    [DataMember, Key(3)] public Moment TouchedAt { get; init; }
    [DataMember, Key(4)] public bool IsPinned { get; init; }
    [DataMember, Key(7)] public string PeerContactName { get; init; } = "";
    [DataMember, Key(8)] public Symbol SystemTag { get; init; }
    [DataMember, Key(9)] public string ExternalContactName { get; init; } = "";
    [DataMember, Key(10)] public ContactState State { get; init; }
    // Populated on backend on reads for peer contacts
    [DataMember, Key(11)] public bool IsBlockedByPeer { get; init; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ContactKind Kind => Id.Kind;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public UserId OwnerId => Id.OwnerId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ChatId ChatId => Id.ChatId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsRegular => State == ContactState.Regular;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsBlocked => State == ContactState.Blocked;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public string? PreferredPeerName => Kind == ContactKind.User
        ? PeerContactName.NullIfWhiteSpace() ?? ExternalContactName.NullIfWhiteSpace()
        : null;

    // Populated on backend on reads
    [DataMember, Key(5)] public Account? Account { get; init; }
    // Populated on front-end on reads
    [DataMember, Key(6)] public Chat.Chat Chat { get; init; } = null!;
}

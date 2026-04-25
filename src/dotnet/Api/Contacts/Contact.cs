using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Contacts;

/// <summary>
/// Represents a user's contact entry (chat, user, or place).
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record Contact(
    [property: DataMember, MemoryPackOrder(0), Key(0)] ContactId Id,
    [property: DataMember, MemoryPackOrder(1), Key(1)] long Version = 0
    ) : IHasId<ContactId>, IHasVersion<long>, IRequirementTarget
{
    public static readonly Requirement<Contact> MustExist = Requirement.New(
        (Contact? c) => c?.Id is not null,
        new(() => StandardError.NotFound<Contact>()));

    [DataMember, MemoryPackOrder(2), Key(2)] public UserId? UserId { get; init; }
    [DataMember, MemoryPackOrder(3), Key(3)] public Moment TouchedAt { get; init; }
    [DataMember, MemoryPackOrder(4), Key(4)] public bool IsPinned { get; init; }
    [DataMember, MemoryPackOrder(7), Key(7)] public string PeerContactName { get; init; } = "";
    [DataMember, MemoryPackOrder(8), Key(8)] public Symbol SystemTag { get; init; }
    [DataMember, MemoryPackOrder(9), Key(9)] public string ExternalContactName { get; init; } = "";
    [DataMember, MemoryPackOrder(10), Key(10)] public ContactState State { get; init; }
    [DataMember, MemoryPackOrder(11), Key(11)] public bool IsBanned { get; init; }
    // Populated on backend on reads for peer contacts
    [DataMember, MemoryPackOrder(12), Key(12)] public bool IsBannedByPeer { get; init; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ContactKind Kind => Id.Kind;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public UserId OwnerId => Id.OwnerId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public ChatId ChatId => Id.ChatId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsRegular => State == ContactState.Regular;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public string? PreferredPeerName => Kind == ContactKind.User
        ? PeerContactName.NullIfWhiteSpace() ?? ExternalContactName.NullIfWhiteSpace()
        : null;

    // Populated on backend on reads
    [DataMember, MemoryPackOrder(5), Key(5)] public Account? Account { get; init; }
    // Populated on front-end on reads
    [DataMember, MemoryPackOrder(6), Key(6)] public Chat.Chat Chat { get; init; } = null!;
}

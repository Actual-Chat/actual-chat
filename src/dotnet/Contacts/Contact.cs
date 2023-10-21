using ActualChat.Users;
using MemoryPack;
using Stl.Fusion.Blazor;
using Stl.Versioning;

namespace ActualChat.Contacts;

[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record Contact(
    [property: DataMember, MemoryPackOrder(0)] ContactId Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0
    ) : IHasId<ContactId>, IHasVersion<long>, IRequirementTarget
{
    public static readonly Requirement<Contact> MustExist = Requirement.New(
        new(() => StandardError.NotFound<Contact>()),
        (Contact? c) => c is { Id.IsNone: false });

    [DataMember, MemoryPackOrder(2)] public UserId UserId { get; init; }
    [DataMember, MemoryPackOrder(3)] public Moment TouchedAt { get; init; }
    [DataMember, MemoryPackOrder(4)] public bool IsPinned { get; init; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ContactKind Kind => Id.ChatId.Kind == ChatKind.Peer ? ContactKind.User : ContactKind.Chat;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public UserId OwnerId => Id.OwnerId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ChatId ChatId => Id.ChatId;

    // Populated on backend on reads
    [DataMember, MemoryPackOrder(5)] public Account? Account { get; init; }
    // Populated on front-end on reads
    [DataMember, MemoryPackOrder(6)] public Chat.Chat Chat { get; init; } = null!;
}

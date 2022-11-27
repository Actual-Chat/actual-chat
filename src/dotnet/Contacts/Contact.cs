using ActualChat.Users;
using Stl.Versioning;

namespace ActualChat.Contacts;

[DataContract]
public sealed record Contact(
    [property: DataMember] ContactId Id,
    [property: DataMember] long Version = 0
    ) : IHasId<ContactId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember] public UserId UserId { get; init; }
    [DataMember] public Moment TouchedAt { get; init; }

    // Shortcuts
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ContactKind Kind => Id.ChatId.Kind == ChatKind.Peer ? ContactKind.User : ContactKind.Chat;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsVirtual => Version == 0;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public UserId OwnerId => Id.OwnerId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatId ChatId => Id.ChatId;

    // Populated on backend on reads
    [DataMember] public Account? Account { get; init; }
    // Populated on front-end on reads
    [DataMember] public Chat.Chat Chat { get; init; } = null!;
}

using ActualChat.Users;
using Stl.Versioning;

namespace ActualChat.Contacts;

[DataContract]
public sealed record Contact : IHasId<ContactId>, IHasVersion<long>, IRequirementTarget
{
    [DataMember] public ContactId Id { get; init; }
    [DataMember] public long Version { get; init; }
    [DataMember] public UserId UserId { get; init; }
    [DataMember] public Moment TouchedAt { get; init; }

    // Shortcuts
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ContactKind Kind => Id.ChatId.Kind == ChatKind.Peer ? ContactKind.User : ContactKind.Chat;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsStored => Version == 0;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public UserId OwnerId => Id.OwnerId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatId ChatId => Id.ChatId;

    // The following properties are populated only on reads
    [DataMember] public Account? Account { get; init; }
    [DataMember] public Chat.Chat Chat { get; init; } = null!;

    public Contact() { }
    public Contact(ContactId id) => Id = id;
}

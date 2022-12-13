using ActualChat.Users;
using Stl.Fusion.Blazor;
using Stl.Versioning;

namespace ActualChat.Contacts;

[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract]
public sealed record Contact(
    [property: DataMember] ContactId Id,
    [property: DataMember] long Version = 0
    ) : IHasId<ContactId>, IHasVersion<long>, IRequirementTarget
{
    public static Contact None { get; } = new(default, 0) { Chat = ActualChat.Chat.Chat.None };
    public static Contact Loading { get; } = new(default, -1) { Chat = ActualChat.Chat.Chat.Loading };

    public static Requirement<Contact> MustExist { get; } = Requirement.New(
        new(() => StandardError.NotFound<Contact>()),
        (Contact? c) => c is { Id.IsNone: false });

    [DataMember] public UserId UserId { get; init; }
    [DataMember] public Moment TouchedAt { get; init; }
    [DataMember] public bool IsPinned { get; init; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ContactKind Kind => Id.ChatId.Kind == ChatKind.Peer ? ContactKind.User : ContactKind.Chat;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsVirtual => Version == 0 || Id.IsNone;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public UserId OwnerId => Id.OwnerId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatId ChatId => Id.ChatId;

    // Populated on backend on reads
    [DataMember] public Account? Account { get; init; }
    // Populated on front-end on reads
    [DataMember] public Chat.Chat Chat { get; init; } = null!;

    public Contact() : this(ContactId.None) { }
}

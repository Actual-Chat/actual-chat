using ActualChat.Users;
using Stl.Versioning;

namespace ActualChat.Contacts;

public enum ContactKind
{
    User = 0,
    Chat = 1,
}

[DataContract]
public record Contact : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    [DataMember] public Symbol Id { get; init; } = Symbol.Empty;
    [DataMember] public long Version { get; init; }
    [DataMember] public Symbol OwnerId { get; init; } = Symbol.Empty;
    [DataMember] public Symbol UserId { get; init; } // Used only on writes
    [DataMember] public Symbol ChatId { get; init; } // Used only on writes
    [DataMember] public Account? Account { get; init; } // Populated only on reads
    [DataMember] public Chat.Chat? Chat { get; init; } // Populated only oh reads

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ContactKind Kind => UserId.IsEmpty ? ContactKind.Chat : ContactKind.User;
}

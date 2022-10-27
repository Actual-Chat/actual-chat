using ActualChat.Users;

namespace ActualChat.Chat;

[DataContract]
[StructLayout(LayoutKind.Auto)]
public readonly struct ChatPrincipalId : IEquatable<ChatPrincipalId>, IHasId<Symbol>
{
    [DataMember]
    public Symbol Id { get; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatPrincipalKind Kind { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public Symbol AuthorId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public Symbol UserId { get; }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public ChatPrincipalId(Symbol id)
    {
        var hasDivider = id.Value.OrdinalContains(':');
        Kind = hasDivider ? ChatPrincipalKind.Author : ChatPrincipalKind.User;
        if (Kind == ChatPrincipalKind.Author) {
            AuthorId = Id = id;
            UserId = default;
        }
        else {
            UserId = Id = id;
            AuthorId = default;
        }
    }

    public ChatPrincipalId(Symbol authorId, Symbol userId)
    {
        if (!authorId.IsEmpty) {
            if (!userId.IsEmpty)
                throw new ArgumentOutOfRangeException(nameof(userId), "Either userId or authorId must be provided, but not both.");
            Kind = ChatPrincipalKind.Author;
            AuthorId = Id = authorId;
            UserId = default;
        }
        else {
            if (userId.IsEmpty)
                throw new ArgumentOutOfRangeException(nameof(userId), "Either userId or authorId must be provided, but not both.");
            Kind = ChatPrincipalKind.User;
            UserId = Id = userId;
            AuthorId = default;
        }
    }

    public void Deconstruct(out Symbol authorId, out Symbol userId)
    {
        authorId = AuthorId;
        userId = UserId;
    }

    // Conversion

    public override string ToString() => Id;
    public static implicit operator ChatPrincipalId(Symbol source) => new(source);
    public static implicit operator ChatPrincipalId(string source) => new(source);
    public static implicit operator Symbol(ChatPrincipalId source) => source.Id;
    public static implicit operator string(ChatPrincipalId source) => source.Id;

    // Equality

    public bool Equals(ChatPrincipalId other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is ChatPrincipalId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(ChatPrincipalId left, ChatPrincipalId right) => left.Equals(right);
    public static bool operator !=(ChatPrincipalId left, ChatPrincipalId right) => !left.Equals(right);
}

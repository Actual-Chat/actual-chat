using ActualChat.Users;

namespace ActualChat.Chat;

#pragma warning disable MA0011

[DataContract]
[StructLayout(LayoutKind.Auto)]
public readonly struct ChatId : IEquatable<ChatId>, IParsable<ChatId>, IRequirementTarget, ICanBeEmpty
{
    public static readonly string PeerChatIdPrefix = "p-";

    [DataMember(Order = 0)]
    public Symbol Id { get; }

    // Set on deserialization
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatKind Kind { get; }
    private UserId UserId1 { get; }
    private UserId UserId2 { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsEmpty => Id.IsEmpty;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public ChatId(Symbol id) => this = Parse(id);
    public ChatId(string id) => this = Parse(id);

    public ChatId(UserId userId1, UserId userId2, SkipValidation _)
    {
        Kind = ChatKind.Peer;
        (UserId1, UserId2) = (userId1, userId2).Sort();
        Id = $"{PeerChatIdPrefix}{UserId1.Value}-{UserId2.Value}";
    }

    public ChatId(Symbol id, UserId userId1, UserId userId2, SkipValidation _)
    {
        Id = id;
        UserId1 = userId1;
        UserId2 = userId2;
        if (userId1.IsEmpty) {
            if (!userId2.IsEmpty)
                throw new ArgumentOutOfRangeException(userId2);
            Kind = ChatKind.Group;
            return;
        }
        if (userId2.IsEmpty)
            throw new ArgumentOutOfRangeException(userId2);
        Kind = ChatKind.Peer;
    }

    public bool IsGroupChatId()
        => Kind == ChatKind.Group;

    public bool IsPeerChatId(UserId ownUserId, out UserId otherUserId)
    {
        if (IsPeerChatId(out var userId1, out var userId2)) {
            otherUserId = (userId1, userId2).OtherThan(ownUserId);
            return true;
        }
        otherUserId = default;
        return false;
    }

    public bool IsPeerChatId(out UserId userId1, out UserId userId2)
    {
        if (Kind == ChatKind.Peer) {
            userId1 = UserId1;
            userId2 = UserId2;
            return true;
        }
        userId1 = userId2 = default;
        return false;
    }

    public ChatId RequireGroupChatId()
        => Kind is ChatKind.Group
            ? this
            : throw StandardError.Constraint("Group chat Id is expected here.");

    public ChatId RequirePeerChatId()
        => Kind is ChatKind.Peer
            ? this
            : throw StandardError.Constraint("Peer chat Id is expected here.");

    // Conversion

    public override string ToString() => Value;
    public static implicit operator Symbol(ChatId source) => source.Id;
    public static implicit operator string(ChatId source) => source.Value;

    public string Shorten(UserId ownUserId)
        => IsPeerChatId(ownUserId, out var otherUserId)
            ? $"{PeerChatIdPrefix}{otherUserId.Value}"
            : Value;

    // Equality

    public bool Equals(ChatId other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is ChatId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(ChatId left, ChatId right) => left.Equals(right);
    public static bool operator !=(ChatId left, ChatId right) => !left.Equals(right);

    // Parsing

    public static ChatId Parse(string s, IFormatProvider? provider)
        => Parse(s);
    public static ChatId Parse(string s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ChatId>();

    public static bool TryParse(string? id, IFormatProvider? provider, out ChatId result)
        => TryParse(id, out result);
    public static bool TryParse(string? s, out ChatId result)
        => TryParse(s, default(UserId), out result);
    public static bool TryParse(string? s, UserId ownUserId, out ChatId result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return false;

        if (!s.OrdinalStartsWith("p-")) {
            // Group chat Id
            foreach (var c in s)
                if (!(char.IsLetterOrDigit(c) || c == '-'))
                    return false;
            result = new ChatId(s, default, default, SkipValidation.Instance);
            return true;
        }

        // Short peer chat Id
        var tail = s.AsSpan(2);
        var dashIndex = tail.IndexOf('-');
        if (dashIndex < 0) {
            if (ownUserId.IsEmpty)
                return false;
            if (!UserId.TryParse(tail.ToString(), out var otherUserId))
                return false;
            var (userId1, userId2) = (ownUserId, otherUserId).Sort();
            result = new ChatId(s, userId1, userId2, SkipValidation.Instance);
            return true;
        }

        // Full peer chat Id
        {
            if (!UserId.TryParse(tail[..dashIndex].ToString(), out var userId1))
                return false;
            if (!UserId.TryParse(tail[(dashIndex + 1)..].ToString(), out var userId2))
                return false;
            result = new ChatId(s, userId1, userId2, SkipValidation.Instance);
            return true;
        }
    }
}

using ActualChat.Users;

namespace ActualChat.Chat;

[DataContract]
[StructLayout(LayoutKind.Auto)]
public readonly struct ParsedChatId : IEquatable<ParsedChatId>, IHasId<Symbol>
{
    private const string PeerChatIdPrefix = "p-";

    public Symbol Id { get; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatIdKind Kind { get; private init; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ParsedUserId UserId1 { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ParsedUserId UserId2 { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsValid => Kind is not ChatIdKind.Invalid;

    public static Symbol FormatShortPeerChatId(Symbol peerUserId)
        => $"{PeerChatIdPrefix}{peerUserId}";

    public static Symbol FormatFullPeerChatId(Symbol userId1, Symbol userId2)
    {
        (userId1, userId2) = (userId1, userId2).Sort();
        return $"{PeerChatIdPrefix}{userId1}-{userId2}";
    }

    public ParsedChatId(Symbol id)
    {
        Id = id;
        UserId1 = Symbol.Empty;
        UserId2 = Symbol.Empty;
        Kind = ChatIdKind.Invalid;
        if (id.IsEmpty)
            return;

        var idValue = Id.Value;
        if (!idValue.OrdinalStartsWith("p-")) {
            // Group chat Id
            foreach (var c in idValue)
                if (!(c == '-' || char.IsLetterOrDigit(c)))
                    return;
            Kind = ChatIdKind.Group;
            return;
        }

        // Peer chat Id
        var tail = idValue.AsSpan(2);
        var dashIndex = tail.IndexOf('-');
        if (dashIndex < 0) {
            UserId1 = tail.ToString();
            if (UserId1.IsValid)
                Kind = ChatIdKind.PeerShort;
            return;
        }

        // Full peer chat Id
        UserId1 = tail[..dashIndex].ToString();
        UserId2 = tail[(dashIndex + 1)..].ToString();
        if (UserId1.IsValid && UserId2.IsValid && UserId1.Id != UserId2.Id)
            Kind = ChatIdKind.PeerFull;
    }

    public ParsedChatId Invalid()
        => this with { Kind = ChatIdKind.Invalid };

    public ParsedChatId RequireValid()
        => IsValid ? this : throw StandardError.Format("Invalid chat Id format.");

    public ParsedChatId RequireGroupChatId()
        => RequireValid().Kind is ChatIdKind.Group
            ? this
            : throw StandardError.Constraint("Group chat Id is expected here.");

    public ParsedChatId RequirePeerChatId()
        => RequireValid().Kind.IsPeerAny()
            ? this
            : throw StandardError.Constraint("Peer chat Id is expected here.");

    public ParsedChatId RequirePeerFullChatId()
        => RequireValid().Kind is ChatIdKind.PeerFull
            ? this
            : throw StandardError.Constraint("Full peer chat Id is expected here.");

    // Conversion

    public override string ToString() => Id;
    public static implicit operator ParsedChatId(Symbol source) => new(source);
    public static implicit operator ParsedChatId(string source) => new(source);
    public static implicit operator Symbol(ParsedChatId source) => source.Id;
    public static implicit operator string(ParsedChatId source) => source.Id;

    public Symbol Shorten(Symbol ownerUserId)
    {
        RequirePeerFullChatId();
        var targetUserId = GetPeerChatTargetUserId(ownerUserId);
        return FormatShortPeerChatId(targetUserId);
    }

    public Symbol GetPeerChatTargetUserId(Symbol ownerUserId)
    {
        switch (Kind) {
        case ChatIdKind.Group:
            return Symbol.Empty;
        case ChatIdKind.PeerShort:
            return UserId1;
        case ChatIdKind.PeerFull:
            var targetUserId = (UserId1.Id, UserId2.Id).OtherThan(ownerUserId);
            if (targetUserId.IsEmpty)
                throw StandardError.Constraint("Specified peer chat Id doesn't belong to the specified user.");
            return targetUserId;
        default:
            throw StandardError.Format("Invalid chat Id format.");
        }
    }

    // Equality

    public bool Equals(ParsedChatId other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is ParsedChatId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(ParsedChatId left, ParsedChatId right) => left.Equals(right);
    public static bool operator !=(ParsedChatId left, ParsedChatId right) => !left.Equals(right);

    // Parsing

    public static bool TryParse(string value, out ParsedChatId result)
    {
        result = new ParsedChatId(value);
        return result.IsValid;
    }

    public static ParsedChatId Parse(string value)
        => new ParsedChatId(value).RequireValid();
}

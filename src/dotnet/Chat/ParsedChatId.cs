using ActualChat.Users;

namespace ActualChat.Chat;

[DataContract]
[StructLayout(LayoutKind.Auto)]
public readonly struct ParsedChatId : IEquatable<ParsedChatId>, IHasId<Symbol>
{
    private const string PeerChatIdPrefix = "p-";

    public Symbol Id { get; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ChatIdKind Kind { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ParsedUserId UserId1 { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public ParsedUserId UserId2 { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsValid { get; }

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
        Kind = ChatIdKind.Group;
        IsValid = false;
        if (id.IsEmpty)
            return;

        var idValue = Id.Value;
        if (!idValue.OrdinalStartsWith("p-")) {
            // Group chat Id
            foreach (var c in idValue)
                if (!(c == '-' || char.IsLetterOrDigit(c)))
                    return;
            Kind = ChatIdKind.Group;
            IsValid = true;
            return;
        }

        // Peer chat Id
        var tail = idValue.AsSpan(2);
        var dashIndex = tail.IndexOf('-');
        if (dashIndex < 0) {
            Kind = ChatIdKind.PeerShort;
            UserId1 = tail.ToString();
            IsValid = UserId1.IsValid;
        }
        else {
            Kind = ChatIdKind.PeerFull;
            UserId1 = tail[..dashIndex].ToString();
            UserId2 = tail[(dashIndex + 1)..].ToString();
            IsValid = UserId1.IsValid && UserId2.IsValid && UserId1.Id != UserId2.Id;
        }
    }

    public ParsedChatId AssertValid()
        => IsValid ? this : throw StandardError.Format("Invalid chat Id format.");

    public ParsedChatId AssertGroup()
        => AssertValid().Kind is ChatIdKind.Group
            ? this
            : throw StandardError.Constraint("Group chat Id is expected here.");

    public ParsedChatId AssertPeerAny()
        => AssertValid().Kind.IsPeerAny()
            ? this
            : throw StandardError.Constraint("Peer chat Id is expected here.");

    public ParsedChatId AssertPeerFull()
        => AssertValid().Kind is ChatIdKind.PeerFull
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
        AssertPeerFull();
        if (ownerUserId == UserId1.Id)
            return FormatShortPeerChatId(UserId2.Id);
        if (ownerUserId == UserId2.Id)
            return FormatShortPeerChatId(UserId1.Id);
        throw StandardError.Constraint("Specified peer chat Id doesn't belong to the specified user.");
    }

    // Equality

    public bool Equals(ParsedChatId other) => Id.Equals(other.Id);
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
        => new ParsedChatId(value).AssertValid();
}

using ActualChat.Chat;
using ActualChat.Users;

namespace ActualChat.Contacts;

[DataContract]
[StructLayout(LayoutKind.Auto)]
public readonly struct ContactId : IEquatable<ContactId>
{
    [DataMember(Order = 0)] public Symbol OwnerId { get; }
    [DataMember(Order = 1)] public Symbol OtherId { get; }
    [DataMember(Order = 2)] public ContactKind Kind { get; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsValid => Kind != ContactKind.Invalid;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsFullyValid => Kind switch {
        ContactKind.User => new ParsedUserId(OwnerId).IsValid && new ParsedUserId(OtherId).IsValid,
        ContactKind.Chat => new ParsedUserId(OwnerId).IsValid && new ParsedChatId(OtherId).IsValid,
        _ => false,
    };

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public ContactId(Symbol ownerId, Symbol otherId, ContactKind kind)
    {
        OwnerId = ownerId;
        OtherId = otherId;
        Kind = kind;
    }

    public ContactId(string id)
    {
        OwnerId = OtherId = default;
        Kind = ContactKind.Invalid;

        var spaceIndex = id.IndexOf(' ');
        if (spaceIndex <= 0 || spaceIndex + 3 >= id.Length)
            return;
        if (id[spaceIndex + 2] != ':')
            return;

        switch (id[spaceIndex + 1]) {
        case 'u':
            Kind = ContactKind.User;
            break;
        case 'c':
            Kind = ContactKind.Chat;
            break;
        default:
            return;
        }

        OwnerId = id[..spaceIndex];
        OtherId = id[(spaceIndex + 3)..];
    }

    public void Deconstruct(out Symbol ownerId, out Symbol otherId)
    {
        ownerId = OwnerId;
        otherId = OtherId;
    }

    public void Deconstruct(out Symbol ownerId, out Symbol otherId, out ContactKind kind)
    {
        ownerId = OwnerId;
        otherId = OtherId;
        kind = Kind;
    }

    public ContactId RequireValid()
        => IsValid ? this : throw StandardError.Format("Invalid contact Id format.");

    public ContactId RequireFullyValid()
        => IsFullyValid ? this : throw StandardError.Format("Invalid contact Id format.");

    public bool IsUserContact(out Symbol userId)
    {
        if (Kind == ContactKind.User) {
            userId = OtherId;
            return true;
        }
        userId = default;
        return false;
    }

    public bool IsChatContact(out Symbol chatId)
    {
        if (Kind == ContactKind.Chat) {
            chatId = OtherId;
            return true;
        }
        chatId = default;
        return false;
    }

    // Conversion

    public string Format()
        => Kind switch {
            ContactKind.User => $"{OwnerId.Value} u:{OtherId.Value}",
            ContactKind.Chat => $"{OwnerId.Value} c:{OtherId.Value}",
            _ => "",
        };

    public override string ToString() => Format();
    public static implicit operator ContactId(Symbol source) => new(source);
    public static implicit operator ContactId(string source) => new(source);
    public static implicit operator Symbol(ContactId source) => source.Format();
    public static implicit operator string(ContactId source) => source.Format();

    public Symbol ToFullChatId()
        => Kind switch {
            ContactKind.User => ParsedChatId.FormatFullPeerChatId(OwnerId, OtherId),
            ContactKind.Chat => OtherId,
            _ => throw StandardError.Format("Invalid contact Id format."),
        };

    public Symbol ToShortChatId()
        => Kind switch {
            ContactKind.User => ParsedChatId.FormatShortPeerChatId(OtherId),
            ContactKind.Chat => OtherId,
            _ => throw StandardError.Format("Invalid contact Id format."),
        };

    // Equality

    public bool Equals(ContactId other)
        => OtherId.Equals(other.OtherId) // Perf: better to compare this property first
            && OwnerId.Equals(other.OwnerId)
            && Kind == other.Kind;
    public override bool Equals(object? obj) => obj is ContactId other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(OwnerId, OtherId, (int)Kind);
    public static bool operator ==(ContactId left, ContactId right) => left.Equals(right);
    public static bool operator !=(ContactId left, ContactId right) => !left.Equals(right);
}

using System.ComponentModel;
using ActualChat.Internal;
using MemoryPack;
using Stl.Fusion.Blazor;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<NotificationId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<NotificationId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<NotificationId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct NotificationId : ISymbolIdentifier<NotificationId>
{
    public static NotificationId None => default;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Symbol Id { get; }

    // Set on deserialization
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public UserId UserId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public NotificationKind Kind { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public Symbol SimilarityKey { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public bool IsNone => Id.IsEmpty;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public NotificationId(Symbol id)
        => this = Parse(id);
    public NotificationId(UserId userId, NotificationKind kind, Symbol similarityKey)
        => this = Parse(Format(userId, kind, similarityKey));
    public NotificationId(UserId userId, NotificationKind kind, Symbol similarityKey, ParseOrNone _)
        => this = ParseOrNone(Format(userId, kind, similarityKey));
    public NotificationId(string id)
        => this = Parse(id);
    public NotificationId(string id, ParseOrNone _)
        => this = ParseOrNone(id);

    public NotificationId(Symbol id, UserId userId, NotificationKind kind, Symbol similarityKey, AssumeValid _)
    {
        if (id.IsEmpty) {
            this = None;
            return;
        }
        Id = id;
        UserId = userId;
        Kind = kind;
        SimilarityKey = similarityKey;
    }

    public NotificationId(UserId userId, NotificationKind kind, Symbol similarityKey, AssumeValid _)
    {
        if (userId.IsNone) {
            this = None;
            return;
        }
        Id = Format(userId, kind, similarityKey);
        UserId = userId;
        Kind = kind;
        SimilarityKey = similarityKey;
    }

    // Conversion

    public override string ToString() => Value;
    public static implicit operator Symbol(NotificationId source) => source.Id;
    public static implicit operator string(NotificationId source) => source.Id.Value;

    // Equality

    public bool Equals(NotificationId other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is NotificationId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(NotificationId left, NotificationId right) => left.Equals(right);
    public static bool operator !=(NotificationId left, NotificationId right) => !left.Equals(right);

    // Parsing

    private static string Format(UserId userId, NotificationKind kind, Symbol similarityKey)
        => userId.IsNone ? "" : $"{userId} {kind.Format()}:{similarityKey.Value}";

    public static NotificationId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<NotificationId>(s);
    public static NotificationId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : StandardError.Format<NotificationId>(s).LogWarning(DefaultLog, None);

    public static bool TryParse(string? s, out NotificationId result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return true; // None

        var userIdLength = s.OrdinalIndexOf(" ");
        if (userIdLength < 0)
            return false;
        if (!UserId.TryParse(s[..userIdLength], out var userId))
            return false;

        var kindStart = userIdLength + 1;
        var kindLength = s.OrdinalIndexOf(":", kindStart);
        if (kindLength < 0)
            return false;

        var sKind = s.AsSpan(kindStart, kindLength - kindStart);
        if (!int.TryParse(sKind, NumberStyles.Integer, CultureInfo.InvariantCulture, out var kind))
            return false;
        if (kind is < 1 or >= (int)NotificationKind.Invalid)
            return false;

        var similarityKey = (Symbol)s[(kindLength + 1)..];
        result = new NotificationId(s, userId, (NotificationKind)kind, similarityKey, AssumeValid.Option);
        return true;
    }
}

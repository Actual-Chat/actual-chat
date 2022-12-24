using System.ComponentModel;
using ActualChat.Internal;
using Stl.Fusion.Blazor;

namespace ActualChat;

[DataContract]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<NotificationId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<NotificationId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<NotificationId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly struct NotificationId : ISymbolIdentifier<NotificationId>
{
    public static NotificationId None => default;

    [DataMember(Order = 0)]
    public Symbol Id { get; }

    // Set on deserialization
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public UserId UserId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public Ulid Ulid { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsNone => Id.IsEmpty;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public NotificationId(Symbol id)
        => this = Parse(id);
    public NotificationId(UserId userId, Ulid ulid)
        => this = Parse(Format(userId, ulid));
    public NotificationId(UserId userId, Ulid ulid, ParseOrNone _)
        => this = ParseOrNone(Format(userId, ulid));
    public NotificationId(string id)
        => this = Parse(id);
    public NotificationId(string id, ParseOrNone _)
        => this = ParseOrNone(id);

    public NotificationId(Symbol id, UserId userId, Ulid ulid, AssumeValid _)
    {
        if (id.IsEmpty) {
            this = None;
            return;
        }
        Id = id;
        UserId = userId;
    }

    public NotificationId(UserId userId, Ulid ulid, AssumeValid _)
    {
        if (userId.IsNone) {
            this = None;
            return;
        }
        Id = Format(userId, ulid);
        UserId = userId;
        Ulid = ulid;
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

    private static string Format(UserId userId, Ulid ulid)
        => userId.IsNone ? "" : $"{userId}:{ulid}";

    public static NotificationId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<NotificationId>();
    public static NotificationId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : default;

    public static bool TryParse(string? s, out NotificationId result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return true; // None

        var userIdLength = s.IndexOf(':');
        if (userIdLength <= 0)
            return false;

        if (!UserId.TryParse(s[..userIdLength], out var ownerId))
            return false;
        if (!Ulid.TryParse(s.AsSpan(userIdLength + 1), out var ulid))
            return false;

        result = new NotificationId(s, ownerId, ulid, AssumeValid.Option);
        return true;
    }
}

using System.ComponentModel;
using MemoryPack;
using ActualLab.Fusion.Blazor;
using ActualLab.Identifiers.Internal;

namespace ActualChat.Notification;

#pragma warning disable CA1036, MA0097 // Implement comparison operators: <, <=, etc.

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<ExplicitNotificationId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<ExplicitNotificationId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<ExplicitNotificationId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct ExplicitNotificationId : ISymbolIdentifier<ExplicitNotificationId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<ExplicitNotificationId>();

    public static ExplicitNotificationId None => default;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Symbol Id { get; }

    // Set on deserialization
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public UserId UserId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ExplicitNotificationKind Kind { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public Symbol SimilarityKey { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => Id.IsEmpty;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public ExplicitNotificationId(Symbol id)
        => this = Parse(id);
    public ExplicitNotificationId(UserId userId, ExplicitNotificationKind kind, Symbol similarityKey)
        => this = Parse(Format(userId, kind, similarityKey));
    public ExplicitNotificationId(UserId userId, ExplicitNotificationKind kind, Symbol similarityKey, ParseOrNone _)
        => this = ParseOrNone(Format(userId, kind, similarityKey));
    public ExplicitNotificationId(string id)
        => this = Parse(id);
    public ExplicitNotificationId(string id, ParseOrNone _)
        => this = ParseOrNone(id);

    public ExplicitNotificationId(Symbol id, UserId userId, ExplicitNotificationKind kind, Symbol similarityKey, AssumeValid _)
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

    public ExplicitNotificationId(UserId userId, ExplicitNotificationKind kind, Symbol similarityKey, AssumeValid _)
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
    public static implicit operator Symbol(ExplicitNotificationId source) => source.Id;
    public static implicit operator string(ExplicitNotificationId source) => source.Id.Value;

    // Equality

    public bool Equals(ExplicitNotificationId other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is ExplicitNotificationId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(ExplicitNotificationId left, ExplicitNotificationId right) => left.Equals(right);
    public static bool operator !=(ExplicitNotificationId left, ExplicitNotificationId right) => !left.Equals(right);

    // Parsing

    private static string Format(UserId userId, ExplicitNotificationKind kind, Symbol similarityKey)
        => userId.IsNone ? "" : $"{userId} {kind.Format()}:{similarityKey.Value}";

    public static ExplicitNotificationId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ExplicitNotificationId>(s);
    public static ExplicitNotificationId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : StandardError.Format<ExplicitNotificationId>(s).LogWarning(Log, None);

    public static bool TryParse(string? s, out ExplicitNotificationId result)
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
        if (!NumberExt.TryParsePositiveInt(sKind, out var kind))
            return false;
        if (kind is < 1 or >= (int)NotificationKind.Invalid)
            return false;

        var similarityKey = (Symbol)s[(kindLength + 1)..];
        result = new ExplicitNotificationId(s, userId, (ExplicitNotificationKind)kind, similarityKey, AssumeValid.Option);
        return true;
    }
}

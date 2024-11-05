using System.ComponentModel;
using MemoryPack;
using ActualChat;
using ActualLab.Fusion.Blazor;
using ActualLab.Identifiers.Internal;

namespace ActualChat.Notification;

#pragma warning disable CA1036, MA0097 // Implement comparison operators: <, <=, etc.

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<ManualNotificationId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<ManualNotificationId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<ManualNotificationId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct ManualNotificationId : ISymbolIdentifier<ManualNotificationId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<ManualNotificationId>();

    public static ManualNotificationId None => default;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Symbol Id { get; }

    // Set on deserialization
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public UserId UserId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public ManualNotificationKind Kind { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public Symbol SimilarityKey { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => Id.IsEmpty;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public ManualNotificationId(Symbol id)
        => this = Parse(id);
    public ManualNotificationId(UserId userId, ManualNotificationKind kind, Symbol similarityKey)
        => this = Parse(Format(userId, kind, similarityKey));
    public ManualNotificationId(UserId userId, ManualNotificationKind kind, Symbol similarityKey, ParseOrNone _)
        => this = ParseOrNone(Format(userId, kind, similarityKey));
    public ManualNotificationId(string id)
        => this = Parse(id);
    public ManualNotificationId(string id, ParseOrNone _)
        => this = ParseOrNone(id);

    public ManualNotificationId(Symbol id, UserId userId, ManualNotificationKind kind, Symbol similarityKey, AssumeValid _)
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

    public ManualNotificationId(UserId userId, ManualNotificationKind kind, Symbol similarityKey, AssumeValid _)
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
    public static implicit operator Symbol(ManualNotificationId source) => source.Id;
    public static implicit operator string(ManualNotificationId source) => source.Id.Value;

    // Equality

    public bool Equals(ManualNotificationId other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is ManualNotificationId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(ManualNotificationId left, ManualNotificationId right) => left.Equals(right);
    public static bool operator !=(ManualNotificationId left, ManualNotificationId right) => !left.Equals(right);

    // Parsing

    private static string Format(UserId userId, ManualNotificationKind kind, Symbol similarityKey)
        => userId.IsNone ? "" : $"{userId} {kind.Format()}:{similarityKey.Value}";

    public static ManualNotificationId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ManualNotificationId>(s);
    public static ManualNotificationId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : StandardError.Format<ManualNotificationId>(s).LogWarning(Log, None);

    public static bool TryParse(string? s, out ManualNotificationId result)
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
        result = new ManualNotificationId(s, userId, (ManualNotificationKind)kind, similarityKey, AssumeValid.Option);
        return true;
    }
}

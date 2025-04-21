using System.ComponentModel;
using MemoryPack;
using ActualLab.Fusion.Blazor;
using ActualLab.Identifiers.Internal;

namespace ActualChat;

#pragma warning disable CA1036, MA0097 // Implement comparison operators: <, <=, etc.

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<PlaceChatId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<PlaceChatId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<PlaceChatId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct PlaceChatId : ISymbolIdentifier<PlaceChatId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<PlaceChatId>();

    public static readonly string IdPrefix = "s-";
    public static PlaceChatId None { get; } = new (AssumeValid.Option);

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Symbol Id { get; }

    // Parsed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public PlaceId PlaceId { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public GroupChatId GroupChatId { get; } = GroupChatId.None;

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Value => Id.Value;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => Id.IsEmpty;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public Symbol LocalChatId => GroupChatId.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsRoot => !IsNone && !IsThread && PlaceId.Id == LocalChatId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public long ThreadId => GroupChatId.ThreadId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsThread => ThreadId > 0;

    public ChatId GetThreadParentOrSelf()
    {
        if (!IsThread)
            return this;

        var parentGroupChat = GroupChatId.ParentChatId;
        return new PlaceChatId(Format(PlaceId, parentGroupChat!.Value),
            PlaceId,
            parentGroupChat,
            AssumeValid.Option);
    }

    public static PlaceChatId Root(PlaceId placeId)
        => new (Format(placeId, placeId.Id), placeId, GroupChatId.Group(placeId.Value), AssumeValid.Option);

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public PlaceChatId(Symbol id) => this = Parse(id);
    public PlaceChatId(string? id) => this = Parse(id);
    public PlaceChatId(string? id, ParseOrNone _) => ParseOrNone(id);
    public PlaceChatId(PlaceId placeId, Generate _)
    {
        if (placeId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(placeId));
        var groupChatId = new GroupChatId(Generate.Option);
        this = new PlaceChatId(Format(placeId, groupChatId.Value), placeId, groupChatId, AssumeValid.Option);
    }

    private PlaceChatId(Symbol id, PlaceId placeId, GroupChatId groupChatId, AssumeValid _)
    {
        if (id.IsEmpty) {
            this = None;
            return;
        }
        Id = id;
        PlaceId = placeId;
        GroupChatId = groupChatId;
    }

    private PlaceChatId(AssumeValid _)
    {
        // NOTE(DF): This constructor should be used to create None instance only.
        Id = "";
        PlaceId = PlaceId.None;
        GroupChatId = GroupChatId.None;
    }

    // Conversion

    public override string ToString() => Value;
    public static implicit operator Symbol(PlaceChatId source) => source.Id;
    public static implicit operator string(PlaceChatId source) => source.Id.Value;

    // Equality

    public bool Equals(PlaceChatId other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is PlaceChatId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(PlaceChatId left, PlaceChatId right) => left.Equals(right);
    public static bool operator !=(PlaceChatId left, PlaceChatId right) => !left.Equals(right);

    // Parsing

    public static string Format(PlaceId placeId, Symbol localChatId)
        => placeId.IsNone ? "" : $"{IdPrefix}{placeId}-{localChatId}";

    public static PlaceChatId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<PlaceChatId>(s);
    public static PlaceChatId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : StandardError.Format<PlaceChatId>(s).LogWarning(Log, None);

    public static bool TryParse(string? s, out PlaceChatId result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return true; // None

        if (!s.OrdinalStartsWith(IdPrefix))
            return false;

        var tail = s.AsSpan(2);
        var placeIdLength = tail.IndexOf('-');
        if (placeIdLength < 0)
            return false;

        if (!PlaceId.TryParse(tail[..placeIdLength].ToString(), out var placeId))
            return false;
        if (!GroupChatId.TryParse(tail[(placeIdLength + 1)..].ToString(), out var groupChatId))
            return false;
        if (placeId.IsNone || groupChatId.IsNone)
            return false; // Both PlaceId and local ChatId must be there

        result = new PlaceChatId((Symbol)s, placeId, groupChatId, AssumeValid.Option);
        return true;
    }
}

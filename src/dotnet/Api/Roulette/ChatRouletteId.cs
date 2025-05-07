using System.ComponentModel;
using ActualLab.Fusion.Blazor;
using ActualLab.Identifiers.Internal;
using MemoryPack;

namespace ActualChat.Roulette;

#pragma warning disable CA1036, MA0097 // Implement comparison operators: <, <=, etc.

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<ChatRouletteId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<ChatRouletteId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<ChatRouletteId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
// TODO(AY): Migrate to StringIdentifier
public readonly partial struct ChatRouletteId : ISymbolIdentifier<ChatRouletteId>
{
    private static readonly Comparer<Symbol> ProfileIdComparer = Comparer<Symbol>.Default;
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<ChatRouletteId>();

    public static ChatRouletteId None => default;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Symbol Id { get; }

    // Parsed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public Symbol ProfileId1 { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public Symbol ProfileId2 { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => Id.IsEmpty;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public ChatRouletteId(Symbol id) => this = Parse(id);
    public ChatRouletteId(string? id) => this = Parse(id);
    public ChatRouletteId(string? id, ParseOrNone _) => ParseOrNone(id);

    public ChatRouletteId(Symbol profileId1, Symbol profileId2, ParseOrNone _)
    {
        if (profileId1.IsEmpty)
            return;
        if (profileId2.IsEmpty)
            return;
        if (profileId1 == profileId2)
            return;

        (ProfileId1, ProfileId2) = (profileId1, profileId2).Sort(ProfileIdComparer);
        Id = Format(ProfileId1, ProfileId2);
    }

    public ChatRouletteId(Symbol profileId1, Symbol profileId2)
    {
        if (profileId1.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(profileId1));
        if (profileId2.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(profileId2));
        if (profileId1 == profileId2)
            throw new ArgumentOutOfRangeException(nameof(profileId2), "Both user IDs are the same.");

        (ProfileId1, ProfileId2) = (profileId1, profileId2).Sort(ProfileIdComparer);
        Id = Format(ProfileId1, ProfileId2);
    }

    public ChatRouletteId(Symbol id, Symbol profileId1, Symbol profileId2, AssumeValid _)
    {
        if (id.IsEmpty) {
            this = None;
            return;
        }
        Id = id;
        ProfileId1 = profileId1;
        ProfileId2 = profileId2;
    }
    // Conversion

    public override string ToString() => Value;
    public static implicit operator Symbol(ChatRouletteId source) => source.Id;
    public static implicit operator string(ChatRouletteId source) => source.Id.Value;

    // Equality

    public bool Equals(ChatRouletteId other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is ChatRouletteId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(ChatRouletteId left, ChatRouletteId right) => left.Equals(right);
    public static bool operator !=(ChatRouletteId left, ChatRouletteId right) => !left.Equals(right);

    // Parsing

    private static string Format(Symbol profileId1, Symbol profileId2)
        => profileId1.IsEmpty ? "" : $"{profileId1}:{profileId2}";

    public static ChatRouletteId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<ChatRouletteId>(s);
    public static ChatRouletteId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : StandardError.Format<ChatRouletteId>(s).LogWarning(Log, None);

    public static bool TryParse(string? s, out ChatRouletteId result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return true; // None

        var profileId1Length = s.IndexOf(':');
        if (profileId1Length < 0)
            return false;

        var profileId1 = (Symbol)s[..profileId1Length];
        var profileId2 = (Symbol)s[(profileId1Length + 1)..];
        if (profileId1.IsEmpty || profileId2.IsEmpty)
            return false; // Both UserIds must be there
        if (string.CompareOrdinal(profileId1.Value, profileId2.Value) >= 0)
            return false; // Wrong sort order or they are the same

        result = new ChatRouletteId((Symbol)s, profileId1, profileId2, AssumeValid.Option);
        return true;
    }
}

using System.ComponentModel;
using MemoryPack;
using ActualLab.Fusion.Blazor;
using ActualLab.Identifiers.Internal;

namespace ActualChat;

#pragma warning disable CA1036, MA0097 // Implement comparison operators: <, <=, etc.

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<UserLinkId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<UserLinkId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<UserLinkId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct UserLinkId : ISymbolIdentifier<UserLinkId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<UserLinkId>();
    public static readonly Alphabet Alphabet = Alphabet.AlphaNumeric + "_";

    public static UserLinkId None => default;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Symbol Id { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => Id.IsEmpty;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public UserLinkId(Symbol id)
        => this = Parse(id);
    public UserLinkId(string? id)
        => this = Parse(id);
    public UserLinkId(string? id, ParseOrNone _)
        => this = ParseOrNone(id);

    private UserLinkId(Symbol id, AssumeValid _)
    {
        if (id.IsEmpty) {
            this = None;
            return;
        }
        Id = id;
    }

    // Conversion

    public override string ToString() => Value;

    // Equality

    public bool Equals(UserLinkId other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is UserLinkId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(UserLinkId left, UserLinkId right) => left.Equals(right);
    public static bool operator !=(UserLinkId left, UserLinkId right) => !left.Equals(right);

    // Parsing

    public static UserLinkId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<UserLinkId>(s);
    public static UserLinkId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : StandardError.Format<UserLinkId>(s).LogWarning(Log, None);

    public static bool TryParse(string? s, out UserLinkId result)
    {
        result = None;
        if (s.IsNullOrEmpty())
            return true; // None

        if (s.Length < 5)
            return false;

        if (!Alphabet.IsMatch(s))
            return false;

        result = new UserLinkId(s, AssumeValid.Option);
        return true;
    }
}

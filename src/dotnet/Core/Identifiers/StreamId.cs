using System.ComponentModel;
using MemoryPack;
using ActualLab.Fusion.Blazor;
using ActualLab.Identifiers.Internal;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<StreamId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<StreamId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<StreamId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct StreamId : ISymbolIdentifier<StreamId>, IHasNodeRef
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= DefaultLogFor<StreamId>();
    private const char Delimiter = '-';

    private static Func<Symbol> LocalIdGenerator { get; } = () => Ulid.NewUlid().ToString();

    public static StreamId None => default;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Symbol Id { get; }

    // Set on deserialization
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public NodeRef NodeRef { get; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public Symbol LocalId { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => Id.IsEmpty;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public StreamId(Symbol id)
        => this = Parse(id);
    public StreamId(string? id)
        => this = Parse(id);
    public StreamId(string? id, ParseOrNone _)
        => this = ParseOrNone(id);
    public StreamId(NodeRef nodeRef, Generate _)
        => this = new StreamId(Format(nodeRef, LocalIdGenerator.Invoke()));
    public StreamId(NodeRef nodeRef, Symbol localId)
        => this = new StreamId(Format(nodeRef, localId));

    public StreamId(Symbol id, NodeRef nodeRef, Symbol localId, AssumeValid _)
    {
        if (id.IsEmpty) {
            this = None;
            return;
        }

        Id = id;
        NodeRef = nodeRef;
        LocalId = localId;
    }

    // Conversion
    public override string ToString() => Value;
    public static implicit operator Symbol(StreamId source) => source.Id;
    public static implicit operator string(StreamId source) => source.Id.Value;

    // Equality
    public bool Equals(StreamId other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is StreamId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(StreamId left, StreamId right) => left.Equals(right);
    public static bool operator !=(StreamId left, StreamId right) => !left.Equals(right);

    // Parsing
    private static string Format(NodeRef nodeRef, Symbol localId)
        => $"{nodeRef}{Delimiter}{localId}";

    public static StreamId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<StreamId>(s);
    public static StreamId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : StandardError.Format<StreamId>(s).LogWarning(Log, None);

    public static bool TryParse(string? s, out StreamId result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return true; // None

        var parts = s.Split(Delimiter, 2);
        if (parts.Length != 2)
            return false;

        if (!NodeRef.TryParse(parts[0], out var nodeRef))
            return false;

        var localId = parts[1];
        if (localId.IsNullOrEmpty() || !Alphabet.AlphaNumericDash.IsMatch(localId))
            return false;

        result = new StreamId(s, nodeRef, localId, AssumeValid.Option);
        return true;
    }
}

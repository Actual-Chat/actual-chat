using System.ComponentModel;
using ActualLab.Generators;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

/// <summary>
/// Unique identifier for a cluster node.
/// </summary>
[DataContract]
[MessagePackFormatter(typeof(Internal.StringLikeMessagePackFormatter<NodeRef>))]
[JsonConverter(typeof(Internal.StringLikeJsonConverter<NodeRef>))]
[Newtonsoft.Json.JsonConverter(typeof(Internal.StringLikeNewtonsoftJsonConverter<NodeRef>))]
[TypeConverter(typeof(Internal.StringLikeTypeConverter<NodeRef>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct NodeRef : ISymbolIdentifier<NodeRef>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<NodeRef>();
    private static RandomStringGenerator IdGenerator => Alphabet.AlphaNumericLower.Generator8;

    public static NodeRef None => default;
    public static readonly NodeRef ThisNodeAlias = new("@", AssumeValid.Option);

    [DataMember(Order = 0)]
    public Symbol Id { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsNone => Id.IsEmpty;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, SerializationConstructor]
    public NodeRef(Symbol id)
        => this = Parse(id);
    public NodeRef(string? id)
        => this = Parse(id);
    public NodeRef(string? id, ParseOrNone _)
        => this = ParseOrNone(id);
    public NodeRef(Generate _)
        => this = new NodeRef(IdGenerator.Next(), AssumeValid.Option);

    public NodeRef(Symbol id, AssumeValid _)
        => Id = id;

    // Conversion

    public override string ToString() => Value;
    public static implicit operator Symbol(NodeRef source) => source.Id;
    public static implicit operator string(NodeRef source) => source.Id.Value;

    // Equality

    public bool Equals(NodeRef other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is NodeRef other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(NodeRef left, NodeRef right) => left.Equals(right);
    public static bool operator !=(NodeRef left, NodeRef right) => !left.Equals(right);

    // Parsing

    public static NodeRef Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<NodeRef>(s);
    public static NodeRef ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : StandardError.Format<NodeRef>(s).LogWarning(Log, None);

    public static bool TryParse(string? s, out NodeRef result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return true; // None

        if (s.Length < 6 || !Alphabet.AlphaNumericDash.IsMatch(s))
            return false;

        result = new NodeRef(s, AssumeValid.Option);
        return true;
    }
}

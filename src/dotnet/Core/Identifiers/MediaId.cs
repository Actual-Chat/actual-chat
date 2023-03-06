using System.ComponentModel;
using ActualChat.Internal;
using Stl.Fusion.Blazor;

namespace ActualChat;

[DataContract]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<MediaId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<MediaId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<MediaId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly struct MediaId : ISymbolIdentifier<MediaId>
{
    public static MediaId None => default;

    [DataMember(Order = 0)]
    public Symbol Id { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsNone => Id.IsEmpty;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public MediaId(Symbol id)
        => this = Parse(id);
    public MediaId(string? id)
        => this = Parse(id);
    public MediaId(string? id, ParseOrNone _)
        => this = ParseOrNone(id);

    public MediaId(Symbol id, AssumeValid _)
    {
        if (id.IsEmpty) {
            this = None;
            return;
        }
        Id = id;
    }

    // Conversion
    public override string ToString() => Value;
    public static implicit operator Symbol(MediaId source) => source.Id;
    public static implicit operator string(MediaId source) => source.Id.Value;

    // Equality
    public bool Equals(MediaId other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is MediaId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(MediaId left, MediaId right) => left.Equals(right);
    public static bool operator !=(MediaId left, MediaId right) => !left.Equals(right);

    // Parsing
    public static MediaId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<MediaId>(s);
    public static MediaId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : StandardError.Format<MediaId>(s).LogWarning(DefaultLog, None);

    public static bool TryParse(string? s, out MediaId result)
    {
        result = default;
        if (s.IsNullOrEmpty())
            return true; // None

        result = new MediaId(s, AssumeValid.Option);
        return true;
    }
}

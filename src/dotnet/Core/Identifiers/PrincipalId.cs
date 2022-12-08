using System.ComponentModel;
using ActualChat.Internal;
using Stl.Fusion.Blazor;

namespace ActualChat;

[DataContract]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<PrincipalId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<PrincipalId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<PrincipalId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly struct PrincipalId : ISymbolIdentifier<PrincipalId>
{
    public static PrincipalId None => default;

    [DataMember(Order = 0)]
    public Symbol Id { get; }

    // Set on deserialization
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public PrincipalKind Kind { get; }
    private AuthorId AuthorId { get; }
    private UserId UserId { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool IsNone => Id.IsEmpty;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor]
    public PrincipalId(Symbol id) => this = Parse(id);
    public PrincipalId(string? id) => this = Parse(id);
    public PrincipalId(string? id, ParseOrNone _) => ParseOrNone(id);

    public PrincipalId(AuthorId authorId, AssumeValid _)
    {
        Id = authorId.Id;
        Kind = PrincipalKind.Author;
        AuthorId = authorId;
        UserId = default;
    }

    public PrincipalId(UserId userId, AssumeValid _)
    {
        Id = userId.Id;
        Kind = PrincipalKind.User;
        AuthorId = default;
        UserId = userId;
    }

    public bool IsAuthor(out AuthorId authorId)
    {
        authorId = AuthorId;
        return Kind == PrincipalKind.Author;
    }

    public bool IsUser(out UserId userId)
    {
        userId = UserId;
        return Kind == PrincipalKind.User;
    }

    // Conversion

    public override string ToString() => Value;
    public static implicit operator Symbol(PrincipalId source) => source.Id;

    // Equality

    public bool Equals(PrincipalId other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is PrincipalId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(PrincipalId left, PrincipalId right) => left.Equals(right);
    public static bool operator !=(PrincipalId left, PrincipalId right) => !left.Equals(right);

    // Parsing

    public static PrincipalId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<PrincipalId>();
    public static PrincipalId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : default;

    public static bool TryParse(string? s, out PrincipalId result)
    {
        if (s.IsNullOrEmpty()) {
            result = default;
            return true; // None
        }

        if (AuthorId.TryParse(s, out var authorId)) {
            result = new PrincipalId(authorId, AssumeValid.Option);
            return true;
        }
        if (UserId.TryParse(s, out var userId)) {
            result = new PrincipalId(userId, AssumeValid.Option);
            return true;
        }
        result = default;
        return false;
    }
}

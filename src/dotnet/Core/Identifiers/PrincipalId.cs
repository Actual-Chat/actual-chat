using System.ComponentModel;
using MemoryPack;
using ActualLab.Fusion.Blazor;
using ActualLab.Identifiers.Internal;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<PrincipalId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<PrincipalId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<PrincipalId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct PrincipalId : ISymbolIdentifier<PrincipalId>
{
    private static ILogger? _log;
    private static ILogger Log => _log ??= StaticLog.For<PrincipalId>();

    public static PrincipalId None => default;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Symbol Id { get; }

    // Set on deserialization
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public PrincipalKind Kind { get; }
    private AuthorId AuthorId { get; }
    private UserId UserId { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => Id.IsEmpty;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public PrincipalId(Symbol id)
        => this = Parse(id);
    public PrincipalId(string? id)
        => this = Parse(id);
    public PrincipalId(string? id, ParseOrNone _)
        => this = ParseOrNone(id);

    public PrincipalId(AuthorId authorId, AssumeValid _)
    {
        if (authorId.IsNone) {
            this = None;
            return;
        }
        Id = authorId.Id;
        Kind = PrincipalKind.Author;
        AuthorId = authorId;
        UserId = default;
    }

    public PrincipalId(UserId userId, AssumeValid _)
    {
        if (userId.IsNone) {
            this = None;
            return;
        }
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
    public static implicit operator string(PrincipalId source) => source.Id.Value;

    // Equality

    public bool Equals(PrincipalId other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is PrincipalId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(PrincipalId left, PrincipalId right) => left.Equals(right);
    public static bool operator !=(PrincipalId left, PrincipalId right) => !left.Equals(right);

    // Parsing

    public static PrincipalId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<PrincipalId>(s);
    public static PrincipalId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : StandardError.Format<PrincipalId>(s).LogWarning(Log, None);

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

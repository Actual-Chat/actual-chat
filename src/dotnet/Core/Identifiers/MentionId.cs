using System.ComponentModel;
using ActualChat.Internal;
using MemoryPack;
using ActualLab.Fusion.Blazor;

namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[JsonConverter(typeof(SymbolIdentifierJsonConverter<MentionId>))]
[Newtonsoft.Json.JsonConverter(typeof(SymbolIdentifierNewtonsoftJsonConverter<MentionId>))]
[TypeConverter(typeof(SymbolIdentifierTypeConverter<MentionId>))]
[ParameterComparer(typeof(ByValueParameterComparer))]
[StructLayout(LayoutKind.Auto)]
public readonly partial struct MentionId : ISymbolIdentifier<MentionId>
{
    public static MentionId None => default;

    [DataMember(Order = 0), MemoryPackOrder(0)]
    public Symbol Id { get; }

    // Set on deserialization
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public PrincipalId PrincipalId { get; }

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public string Value => Id.Value;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => Id.IsEmpty;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public PrincipalKind Kind => PrincipalId.Kind;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public AuthorId AuthorId => PrincipalId.IsAuthor(out var authorId) ? authorId : AuthorId.None;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public UserId UserId => PrincipalId.IsUser(out var userId) ? userId : UserId.None;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public MentionId(Symbol id)
        => this = Parse(id);
    public MentionId(string? id)
        => this = Parse(id);
    public MentionId(string? id, ParseOrNone _)
        => this = ParseOrNone(id);

    public MentionId(AuthorId authorId, AssumeValid _) : this(new PrincipalId(authorId, _), _)
    { }

    public MentionId(UserId userId, AssumeValid _) : this(new PrincipalId(userId, _), _)
    { }

    public MentionId(PrincipalId principalId, AssumeValid _)
    {
        if (principalId.IsNone) {
            this = None;
            return;
        }
        Id = principalId.Kind switch {
            PrincipalKind.Author => $"a:{principalId}",
            PrincipalKind.User => $"u:{principalId}",
            _ => throw new ArgumentOutOfRangeException($"{nameof(principalId)}.{nameof(principalId.Kind)}", principalId.Kind, null),
        };
        PrincipalId = principalId;
    }

    public bool IsAuthor(out AuthorId authorId)
        => PrincipalId.IsAuthor(out authorId);

    public bool IsUser(out UserId userId)
        => PrincipalId.IsUser(out userId);

    // Conversion

    public override string ToString() => Value;
    public static implicit operator Symbol(MentionId source) => source.Id;
    public static implicit operator string(MentionId source) => source.Id.Value;

    // Equality

    public bool Equals(MentionId other) => Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is MentionId other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
    public static bool operator ==(MentionId left, MentionId right) => left.Equals(right);
    public static bool operator !=(MentionId left, MentionId right) => !left.Equals(right);

    // Parsing

    public static MentionId Parse(string? s)
        => TryParse(s, out var result) ? result : throw StandardError.Format<MentionId>(s);
    public static MentionId ParseOrNone(string? s)
        => TryParse(s, out var result) ? result : None;

    public static bool TryParse(string? s, out MentionId result)
    {
        if (s.IsNullOrEmpty() || s.Length < 2) {
            result = default;
            return true; // None
        }

        switch (s[..2]) {
            case "a:" when AuthorId.TryParse(s[2..], out var authorId):
                result = new MentionId(authorId, AssumeValid.Option);
                return true;
            case "u:" when UserId.TryParse(s[2..], out var userId):
                result = new MentionId(userId, AssumeValid.Option);
                return true;
            default:
                result = default;
                return false;
        }
    }
}

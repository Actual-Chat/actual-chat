using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Extended <see cref="Author"/> with user association and role memberships.
/// </summary>
[DataContract, MessagePackObject(AllowPrivate = true)]
[ParameterComparer(typeof(ByRefParameterComparer))]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor]
public sealed partial record AuthorFull(
    [property: DataMember, Key(10)] UserId UserId,
    AuthorId Id, long Version = 0
    ) : Author(Id, Version)
{
    public static new readonly Requirement<AuthorFull> MustExist = Requirement.New(
        (AuthorFull? a) => a?.Id is not null,
        new(() => StandardError.NotFound<Author>()));

    [DataMember, Key(11)]  public IReadOnlyList<RoleId> RoleIds { get; init; } = [];
    [DataMember, Key(13)] public bool IsPlaceAuthor { get; set; }
    [DataMember, Key(12)]  public Moment CreatedAt { get; init; }

    // MessagePack deserialization entry point: the int-keyed positional record ctor's UserId-first
    // parameter order doesn't match Key(0)'s expected type, so MessagePack falls through to this
    // parameterless ctor and assigns each Key via the property initializers.
    [SerializationConstructor]
    internal AuthorFull() : this(null!, null!) { }

    // This record relies on referential equality
    public bool Equals(AuthorFull? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

/// <summary>
/// Represents changes to an <see cref="Author"/> for incremental updates.
/// </summary>
[DataContract, MessagePackObject(true)]
public sealed partial record AuthorDiff : RecordDiff
{
    [DataMember] public Symbol? AvatarId { get; init; }
    [DataMember] public bool? IsAnonymous { get; init; }
    [DataMember] public bool? HasLeft { get; init; }
}

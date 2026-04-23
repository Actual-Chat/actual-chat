using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Extended <see cref="Author"/> with user association and role memberships.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByRefParameterComparer))]
[method: ConstructorShape, JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
public sealed partial record AuthorFull(
    [property: DataMember, MemoryPackOrder(6), Key(10)] UserId UserId,
    AuthorId Id,
    long Version = 0
    ) : Author(Id, Version)
{
    public static new readonly Requirement<AuthorFull> MustExist = Requirement.New(
        (AuthorFull? a) => a?.Id is not null,
        new(() => StandardError.NotFound<Author>()));

    [DataMember, MemoryPackOrder(7), Key(11)] public IReadOnlyList<RoleId> RoleIds { get; init; } = [];
    [DataMember, MemoryPackOrder(10), Key(13)] public bool IsPlaceAuthor { get; set; }
    [DataMember, MemoryPackOrder(9), Key(12)] public Moment CreatedAt { get; init; }

    private AuthorFull() : this(null!, null!) { }

    // This record relies on referential equality
    public bool Equals(AuthorFull? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

/// <summary>
/// Represents changes to an <see cref="Author"/> for incremental updates.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record AuthorDiff : RecordDiff
{
    [DataMember, MemoryPackOrder(0)] public Symbol? AvatarId { get; init; }
    [DataMember, MemoryPackOrder(1)] public bool? IsAnonymous { get; init; }
    [DataMember, MemoryPackOrder(2)] public bool? HasLeft { get; init; }
}

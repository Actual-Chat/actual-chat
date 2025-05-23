using ActualLab.Fusion.Blazor;
using MemoryPack;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByRefParameterComparer))]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
public sealed partial record AuthorFull(
    [property: DataMember, MemoryPackOrder(6)] UserId UserId,
    AuthorId Id, long Version = 0
    ) : Author(Id, Version)
{
    public static new readonly Requirement<AuthorFull> MustExist = Requirement.New(
        (AuthorFull? a) => a?.Id is not null,
        new(() => StandardError.NotFound<Author>()));

    [DataMember, MemoryPackOrder(7)]  public IReadOnlyList<RoleId> RoleIds { get; init; } = [];
    [DataMember, MemoryPackOrder(10)] public bool IsPlaceAuthor { get; set; }
    [DataMember, MemoryPackOrder(9)]  public Moment CreatedAt { get; init; }

    private AuthorFull() : this(null!, null!) { }

    // This record relies on referential equality
    public bool Equals(AuthorFull? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record AuthorDiff : RecordDiff
{
    [DataMember, MemoryPackOrder(0)] public Symbol? AvatarId { get; init; }
    [DataMember, MemoryPackOrder(1)] public bool? IsAnonymous { get; init; }
    [DataMember, MemoryPackOrder(2)] public bool? HasLeft { get; init; }
}

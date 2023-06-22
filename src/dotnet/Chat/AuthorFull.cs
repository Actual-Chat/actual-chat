using ActualChat.Users;
using MemoryPack;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record AuthorFull(AuthorId Id, long Version = 0) : Author(Id, Version)
{
    public static new Requirement<AuthorFull> MustExist { get; } = Requirement.New(
        new(() => StandardError.NotFound<Author>()),
        (AuthorFull? a) => a is { Id.IsNone: false });

    public static new AuthorFull None { get; } = new() { Avatar = Avatar.None };
    public static new AuthorFull Loading { get; } = new(default, -1) { Avatar = Avatar.Loading }; // Should differ by Id & Version from None

    [DataMember, MemoryPackOrder(6)] public UserId UserId { get; init; }
    [DataMember, MemoryPackOrder(7)] public ImmutableArray<Symbol> RoleIds { get; init; } = ImmutableArray<Symbol>.Empty;

    private AuthorFull() : this(default, 0) { }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public AuthorFull(UserId userId, AuthorId id, long version = 0) : this(id, version)
        => UserId = userId;

    // This record relies on version-based equality
    public bool Equals(AuthorFull? other) => EqualityComparer.Equals(this, other);
    public override int GetHashCode() => EqualityComparer.GetHashCode(this);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record AuthorDiff : RecordDiff
{
    [DataMember, MemoryPackOrder(0)] public Symbol? AvatarId { get; init; }
    [DataMember, MemoryPackOrder(1)] public bool? IsAnonymous { get; init; }
    [DataMember, MemoryPackOrder(2)] public bool? HasLeft { get; init; }
}

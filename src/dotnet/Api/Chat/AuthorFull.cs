using ActualChat.Users;
using MemoryPack;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record AuthorFull(AuthorId Id, long Version = 0) : Author(Id, Version)
{
    public static new readonly Requirement<AuthorFull> MustExist = Requirement.New(
        new(() => StandardError.NotFound<Author>()),
        (AuthorFull? a) => a is { Id.IsNone: false });

    public static new readonly AuthorFull None = new() { Avatar = Avatar.None };
    public static new readonly AuthorFull Loading = new(default, -1) { Avatar = Avatar.Loading }; // Should differ by Id & Version from None

    [DataMember, MemoryPackOrder(6)] public UserId UserId { get; init; }
    [DataMember, MemoryPackOrder(7)] public ApiArray<Symbol> RoleIds { get; init; }

    private AuthorFull() : this(default, 0) { }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public AuthorFull(UserId userId, AuthorId id, long version = 0) : this(id, version)
        => UserId = userId;

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

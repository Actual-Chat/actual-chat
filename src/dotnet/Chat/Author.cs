using ActualChat.Comparison;
using ActualChat.Users;
using MemoryPack;
using Stl.Versioning;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record Author(
    [property: DataMember, MemoryPackOrder(0)] AuthorId Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0
    ): IHasId<AuthorId>, IHasVersion<long>, IRequirementTarget
{
    public static IdAndVersionEqualityComparer<Author, AuthorId> EqualityComparer { get; } = new();

    public static Author None { get; } = new() { Avatar = Avatar.None };
    public static Author Loading { get; } = new(default, -1) { Avatar = Avatar.Loading }; // Should differ by Id & Version from None

    public static Requirement<Author> MustExist { get; } = Requirement.New(
        new(() => StandardError.NotFound<Author>()),
        (Author? a) => a is { Id.IsNone: false });

    [DataMember, MemoryPackOrder(2)] public Symbol AvatarId { get; init; }
    [DataMember, MemoryPackOrder(3)] public bool IsAnonymous { get; init; }
    [DataMember, MemoryPackOrder(4)] public bool HasLeft { get; init; }

    // Populated on reads by AuthorsBackend
    [DataMember, MemoryPackOrder(5)] public Avatar Avatar { get; init; } = null!;

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public ChatId ChatId => Id.ChatId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, MemoryPackIgnore]
    public long LocalId => Id.LocalId;

    private Author() : this(default, 0) { }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor]
    public Author(Symbol avatarId, bool isAnonymous, bool hasLeft, Avatar avatar, AuthorId id, long version = 0)
        : this(id, version)
    {
        AvatarId = avatarId;
        IsAnonymous = isAnonymous;
        HasLeft = hasLeft;
        Avatar = avatar;
    }

    // This record relies on version-based equality
    public virtual bool Equals(Author? other) => EqualityComparer.Equals(this, other);
    public override int GetHashCode() => EqualityComparer.GetHashCode(this);
}

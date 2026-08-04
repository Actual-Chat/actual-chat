using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Chat;

/// <summary>
/// Represents a chat participant with an avatar identity.
/// </summary>
[DataContract, MessagePackObject]
[ParameterComparer(typeof(ByRefParameterComparer))]
public partial record Author(
    [property: DataMember, Key(0)] AuthorId Id,
    [property: DataMember, Key(1)] long Version = 0
    ): IHasId<AuthorId>, IHasVersion<long>, IRequirementTarget
{
    public static readonly Requirement<Author> MustExist = Requirement.New(
        (Author? a) => a?.Id is not null,
        new(() => StandardError.NotFound<Author>()));

    [DataMember, Key(2)] public Symbol AvatarId { get; init; }
    [DataMember, Key(3)] public bool IsAnonymous { get; init; }
    [DataMember, Key(4)] public bool HasLeft { get; init; }

    // Populated on reads by AuthorsBackend
    [DataMember, Key(5)] public Avatar Avatar { get; init; } = null!;

    // Computed
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ChatId ChatId => Id.ChatId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public long LocalId => Id.LocalId;

    private Author() : this(null!, 0) { }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, SerializationConstructor]
    public Author(AuthorId id, long version, Symbol avatarId, bool isAnonymous, bool hasLeft, Avatar avatar)
        : this(id, version)
    {
        AvatarId = avatarId;
        IsAnonymous = isAnonymous;
        HasLeft = hasLeft;
        Avatar = avatar;
    }

    // This record relies on referential equality
    public virtual bool Equals(Author? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Users;

/// <summary>
/// Represents a user's avatar with name, picture, and bio information.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
[ParameterComparer(typeof(ByRefParameterComparer))]
public partial record Avatar(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Symbol Id,
    [property: DataMember, MemoryPackOrder(1), Key(1)] long Version = 0
    ) : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    public const string GuestName = "Guest";

    public static readonly Requirement<Avatar> MustExist = Requirement.New(
        (Avatar? a) => a is { Id.IsEmpty : false },
        new(() => StandardError.NotFound<Avatar>()));

    [DataMember, MemoryPackOrder(2), Key(2)] public string Name { get; init; } = "";
    [DataMember, MemoryPackOrder(3), Key(3)] public string PictureUrl { get; init; } = "";
    [DataMember, MemoryPackOrder(4), Key(4)] public MediaId? MediaId { get; init; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public Picture? Picture => Media.ToPicture(PictureUrl, AvatarKey);
    [DataMember, MemoryPackOrder(5), Key(5)] public string Bio { get; init; } = "";
    [DataMember, MemoryPackOrder(9), Key(6)] public string AvatarKey { get; init; } = "";

    // Populated only on reads
    [DataMember, MemoryPackOrder(6), Key(7)] public Media.Media? Media { get; init; }

    // Helpers

    public Avatar WithMissingPropertiesFrom(Avatar? other)
    {
        if (other == null)
            return this;

        var avatar = this;
        if (avatar.Name.IsNullOrEmpty())
            avatar = avatar with { Name = other.Name };
        if (avatar.Bio.IsNullOrEmpty())
            avatar = avatar with { Bio = other.Bio };
        if (avatar.MediaId == null)
            avatar = avatar with { MediaId = other.MediaId };
        if (avatar.PictureUrl.IsNullOrEmpty())
            avatar = avatar with { PictureUrl = other.PictureUrl };
        if (avatar.AvatarKey.IsNullOrEmpty())
            avatar = avatar with { AvatarKey = other.AvatarKey };
        return avatar;
    }

    public Avatar WithPicture(Picture? picture)
    {
        if (picture is null)
            return this;

        return this with {
            MediaId = picture.MediaRef?.MediaId,
            PictureUrl = picture.MediaRef is null ? picture.ExternalUrl ?? "" : "",
            AvatarKey = picture.MediaRef is null ? picture.AvatarKey ?? "" : "",
        };
    }

    // This record relies on referential equality
    public virtual bool Equals(Avatar? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record AvatarDiff : RecordDiff
{
    [DataMember, MemoryPackOrder(0)] public string? Name { get; init; }
    [DataMember, MemoryPackOrder(1)] public string? Bio { get; init; }
    [DataMember, MemoryPackOrder(2)] public MediaId? MediaId { get; init; }
    [DataMember, MemoryPackOrder(3)] public string? PictureUrl { get; init; }
    [DataMember, MemoryPackOrder(4)] public string? AvatarKey { get; init; }
    [DataMember, MemoryPackOrder(5)] public UserId? UserId { get; init; }
    [DataMember, MemoryPackOrder(6)] public bool? IsAnonymous { get; init; }

    public static AvatarDiff FromFull(AvatarFull avatar)
        => new() {
            Name = avatar.Name,
            Bio = avatar.Bio,
            MediaId = avatar.MediaId,
            PictureUrl = avatar.PictureUrl,
            AvatarKey = avatar.AvatarKey,
            UserId = avatar.UserId,
            IsAnonymous = avatar.IsAnonymous,
        };

    public AvatarDiff WithMissingPropertiesFrom(AvatarFull other)
        => new() {
            Name = Name ?? other.Name,
            Bio = Bio ?? other.Bio,
            MediaId = MediaId ?? other.MediaId,
            PictureUrl = PictureUrl ?? other.PictureUrl,
            AvatarKey = AvatarKey ?? other.AvatarKey,
            UserId = UserId ?? other.UserId,
            IsAnonymous = IsAnonymous ?? other.IsAnonymous,
        };
}

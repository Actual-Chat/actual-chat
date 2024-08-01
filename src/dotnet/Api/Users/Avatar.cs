﻿using ActualChat.Media;
using MemoryPack;
using ActualLab.Fusion.Blazor;
using ActualLab.Versioning;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[ParameterComparer(typeof(ByRefParameterComparer))]
public partial record Avatar(
    [property: DataMember, MemoryPackOrder(0)] Symbol Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0
    ) : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    public const string GuestName = "Guest";
    public static Avatar None { get; } = new(Symbol.Empty, 0);
    public static readonly Avatar Loading = new(Symbol.Empty, -1); // Should differ by ref. from None

    public static readonly Requirement<Avatar> MustExist = Requirement.New(
        (Avatar? a) => a is { Id.IsEmpty : false },
        new(() => StandardError.NotFound<Avatar>()));

    [DataMember, MemoryPackOrder(2)] public string Name { get; init; } = "";
    [DataMember, MemoryPackOrder(3)] public string PictureUrl { get; init; } = "";
    [DataMember, MemoryPackOrder(4)] public MediaId MediaId { get; init; }
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public Picture? Picture => Media.ToPicture(PictureUrl, AvatarKey);
    [DataMember, MemoryPackOrder(5)] public string Bio { get; init; } = "";
    [DataMember, MemoryPackOrder(9)] public string AvatarKey { get; init; } = "";

    // Populated only on reads
    [DataMember, MemoryPackOrder(6)] public Media.Media? Media { get; init; }

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
        if (avatar.MediaId.IsNone)
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
            MediaId = picture.MediaContent?.MediaId ?? MediaId.None,
            PictureUrl = picture.MediaContent is null ? picture.ExternalUrl ?? "" : "",
            AvatarKey = picture.MediaContent is null ? picture.AvatarKey ?? "" : "",
        };
    }

    // This record relies on referential equality
    public virtual bool Equals(Avatar? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}

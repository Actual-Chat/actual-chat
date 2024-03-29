﻿using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ActualLab.Generators;
using ActualLab.Versioning;

namespace ActualChat.Users.Db;

[Table("Avatars")]
public class DbAvatar : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    public static readonly RandomStringGenerator IdGenerator = new(10, Alphabet.AlphaNumeric);

    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public string? UserId { get; set; }
    public string Name { get; set; } = "";
    public string Picture { get; set; } = "";
    public string MediaId { get; set; } = "";
    public string AvatarKey { get; set; } = "";
    public string Bio { get; set; } = "";
    public bool IsAnonymous { get; set; }

    public DbAvatar() { }
    public DbAvatar(AvatarFull model) => UpdateFrom(model);

    public AvatarFull ToModel()
        => new(new UserId(UserId), Id, Version) {
            Name = Name,
            MediaId = new MediaId(MediaId),
            Bio = Bio,
            PictureUrl = Picture,
            AvatarKey = AvatarKey,
            IsAnonymous = IsAnonymous,
        };

    public void UpdateFrom(AvatarFull model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id);
        model.RequireSomeVersion();
        var isNew = Id.IsNullOrEmpty();

        if (UserId.IsNullOrEmpty())
            UserId = model.UserId.Value.NullIfEmpty();
        else if (model.UserId != (Symbol)UserId)
            throw StandardError.Constraint("Can't change Avatar.UserId.");

        Id = id;
        Version = model.Version;
        Name = model.Name;
        MediaId = model.MediaId;
        Bio = model.Bio;
        Picture = model.PictureUrl;
        AvatarKey = model.AvatarKey;
        if (isNew)
            IsAnonymous = model.IsAnonymous;
        else if (IsAnonymous != model.IsAnonymous)
            throw StandardError.Constraint("Can't change Avatar.IsAnonymous.");
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbAvatar>
    {
        public void Configure(EntityTypeBuilder<DbAvatar> builder)
            => builder.Property(a => a.Id).IsRequired();
    }
}

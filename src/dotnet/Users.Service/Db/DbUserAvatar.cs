using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Users.Db;

public enum UserAvatarType { User = 0, AnonymousChatAuthor = 1 }

/// <summary>
/// User avatar. <br />
/// </summary>
[Table("UserAvatars")]
public class DbUserAvatar : IHasId<string>, IRequirementTarget
{
    string IHasId<string>.Id => Id;
    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public string UserId { get; set; } = null!;
    public long LocalId { get; set; }

    public string Name { get; set; } = "";
    public string Picture { get; set; } = "";
    public string Bio { get; set; } = "";

    public DbUserAvatar() { }
    public DbUserAvatar(UserAvatar model) => UpdateFrom(model);

    public static string ComposeId(string principalId, UserAvatarType avatarType, long localId)
        => $"{avatarType:D}:{principalId}:{localId.ToString(CultureInfo.InvariantCulture)}";

    public UserAvatar ToModel()
        => new() {
            Id = Id,
            Version = Version,
            UserId = UserId,
            Name = Name,
            Picture = Picture,
            Bio = Bio
        };

    public void UpdateFrom(UserAvatar model)
    {
        Version = model.Version;
        UserId = model.UserId.Value;
        Name = model.Name;
        Picture = model.Picture;
        Bio = model.Bio;
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbUserAvatar>
    {
        public void Configure(EntityTypeBuilder<DbUserAvatar> builder)
            => builder.Property(a => a.Id).IsRequired();
    }
}

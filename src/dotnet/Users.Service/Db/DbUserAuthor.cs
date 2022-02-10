using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Users.Db;

/// <summary>
/// Primary author of an user. <br />
/// </summary>
[Table("UserAuthors")]
public class DbUserAuthor : IHasId<string>
{
    [Key] public string UserId { get; set; } = null!;
    string IHasId<string>.Id => UserId;

    [ConcurrencyCheck] public long Version { get; set; }
    public string Name { get; set; } = "";
    public bool IsAnonymous { get; set; }
    public string AvatarId { get; set; } = "";

    public DbUserAuthor() { }
    public DbUserAuthor(UserAuthor model) => UpdateFrom(model);

    public UserAuthor ToModel()
        => new() {
            Id = UserId,
            Version = Version,
            Name = Name,
            IsAnonymous = IsAnonymous,
            AvatarId = AvatarId
        };

    public void UpdateFrom(UserAuthor model)
    {
        UserId = model.Id;
        Version = model.Version;
        Name = model.Name;
        IsAnonymous = model.IsAnonymous;
        AvatarId = model.AvatarId;
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbUserAuthor>
    {
        public void Configure(EntityTypeBuilder<DbUserAuthor> builder)
        {
            builder.Property(a => a.UserId).IsRequired();
        }
    }
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Users.Db;

/// <summary>
/// Primary author of an user. <br />
/// </summary>
[Table("UserAuthors")]
public class DbUserAuthor : IAuthorLike
{
    [Key] public string UserId { get; set; } = null!;
    AuthorId IHasId<AuthorId>.Id => UserId;

    public long Version { get; set; }
    public string Name { get; set; } = "";
    public string Picture { get; set; } = "";
    public bool IsAnonymous { get; set; }

    public UserAuthor ToModel()
        => new() {
            Id = UserId,
            Version = Version,
            Name = Name,
            Picture = Picture,
            IsAnonymous = IsAnonymous,
        };

    internal class EntityConfiguration : IEntityTypeConfiguration<DbUserAuthor>
    {
        public void Configure(EntityTypeBuilder<DbUserAuthor> builder)
        {
            builder.Property(a => a.UserId).IsRequired();
            builder.Property(a => a.Version).IsConcurrencyToken();
        }
    }
}

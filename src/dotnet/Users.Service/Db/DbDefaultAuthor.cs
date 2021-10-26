using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Users.Db;

/// <summary>
/// Defaults for an author object which represents a user to a chat. <br />
/// It shouldn't be linked with an auth activities, it's only "view" of an user (avatar).
/// </summary>
public class DbDefaultAuthor : IAuthorInfo
{
    public long Id { get; set; }

    public string UserId { get; set; } = "";

    /// <summary> The url of the author avatar. </summary>
    public string Picture { get; set; } = "";

    /// <summary> @{Nickame}, e.g. @ivan </summary>
    public string Nickname { get; set; } = "";

    /// <summary> e.g. Ivan Ivanov </summary>
    public string Name { get; set; } = "";

    /// <summary> Is user want to be anonymous in chats by default. </summary>
    public bool IsAnonymous { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbDefaultAuthor>
    {
        public void Configure(EntityTypeBuilder<DbDefaultAuthor> builder)
        {
            builder.ToTable("DefaultAuthors");
            builder.HasOne<DbUser>().WithOne(u => u.DefaultAuthor).HasForeignKey<DbDefaultAuthor>(a => a.UserId);
        }
    }

    public DefaultAuthor ToModel() => new() {
        Name = Name,
        Nickname = Nickname,
        Picture = Picture,
        IsAnonymous = IsAnonymous,
    };
}

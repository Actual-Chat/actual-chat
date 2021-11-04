using System.ComponentModel.DataAnnotations;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stl.Versioning;

namespace ActualChat.Chat.Db;

/// <summary>
/// The author object which represents a user to a chat. <br />
/// It shouldn't be linked with an auth activities, it's only "view" of an user (avatar).
/// </summary>
public class DbAuthor : IAuthorInfo, IHasId<string>, IHasVersion<long>
{
    public string Id { get; set; } = null!;
    public long Version { get; set; }
    /// <inheritdoc />
    public string? Picture { get; set; }
    /// <inheritdoc />
    public string? Nickname { get; set; }
    /// <inheritdoc />
    public string? Name { get; set; }
    /// <inheritdoc />
    /// TODO: move this to the <see cref="DbChatUser"/> (?)
    public bool IsAnonymous { get; set; }

    public DbAuthor() { }

    public DbAuthor(IAuthorInfo info)
    {
        Name = info.Name;
        Nickname = info.Nickname;
        Picture = info.Picture;
        IsAnonymous = info.IsAnonymous;
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbAuthor>
    {
        public void Configure(EntityTypeBuilder<DbAuthor> builder)
        {
            builder.ToTable("Authors");
            builder.HasKey(a => a.Id);
            builder.Property(a => a.Version).IsConcurrencyToken();
            builder.Property(a => a.Id).ValueGeneratedOnAdd().HasValueGenerator<UlidValueGenerator>();
            builder.HasMany<DbChatEntry>().WithOne(x => x.Author).HasForeignKey(x => x.AuthorId);
        }
    }
}
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stl.Versioning;

namespace ActualChat.Chat.Db;

public class DbChatUser : IHasId<string>, IHasVersion<long>
{
    public string Id { get; set; } = null!;
    public long Version { get; set; }
    public string AuthorId { get; set; } = "";
    /// <summary>
    /// If <see langword="null" /> the author's owner (user) isn't authenticated.
    /// </summary>
    public string? UserId { get; set; }
    public string ChatId { get; set; } = "";

    public DbAuthor Author { get; set; } = null!;

    internal class EntityConfiguration : IEntityTypeConfiguration<DbChatUser>
    {
        public void Configure(EntityTypeBuilder<DbChatUser> builder)
        {
            builder.ToTable("Users");
            builder.HasKey(u => u.Id);
            builder.HasIndex(u => new { u.UserId, u.ChatId }).IncludeProperties(u => u.AuthorId);
            builder.HasIndex(u => u.AuthorId);
            builder.Property(u => u.Version).IsConcurrencyToken();
            builder.Property(u => u.Id).ValueGeneratedOnAdd().HasValueGenerator<UlidValueGenerator>();
            builder.HasOne<DbChat>().WithMany(c => c.Users).HasForeignKey(x => x.ChatId);
            builder.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId);
        }
    }
}

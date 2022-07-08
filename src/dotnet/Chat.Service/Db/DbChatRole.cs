using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Chat.Db;

[Table("ChatRoles")]
[Index(nameof(ChatId), nameof(LocalId))]
[Index(nameof(ChatId), nameof(Name))]
public class DbChatRole : IHasId<string>
{
    [Key] public string Id { get; set; } = null!;
    string IHasId<string>.Id => Id;

    public string ChatId { get; set; } = null!;
    public long LocalId { get; set; }

    [ConcurrencyCheck] public long Version { get; set; }
    public string Name { get; set; } = "";
    public string Picture { get; set; } = "";
    public string AuthorIds { get; set; } = ""; // Space-separated

    public DbChatRole() { }
    public DbChatRole(ChatRole model) => UpdateFrom(model);

    public ChatRole ToModel()
        => new(Id) {
            Id = Id,
            Version = Version,
            Name = Name,
            Picture = Picture,
            AuthorIds = AuthorIds.Split(" ").Select(x => new Symbol(x)).ToImmutableHashSet(),
        };

    public void UpdateFrom(ChatRole model)
    {
        if (!model.IsPersistent)
            throw new InvalidOperationException("Can't persist non-persistent chat role.");
        var parsedRoleId = new ParsedChatRoleId(model.Id).AssertValid();
        Id = model.Id;
        ChatId = parsedRoleId.ChatId;
        LocalId = parsedRoleId.LocalId;
        Version = model.Version;
        Name = model.Name;
        Picture = model.Picture;
        AuthorIds = model.AuthorIds.ToDelimitedString(" ");
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbChatAuthor>
    {
        public void Configure(EntityTypeBuilder<DbChatAuthor> builder)
        {
            builder.Property(a => a.Id).IsRequired();
            builder.Property(a => a.ChatId).IsRequired();
            builder.Property(a => a.Name).IsRequired();
        }
    }
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stl.Versioning;

namespace ActualChat.Chat.Db;

[Table("Chats")]
public class DbChat : IHasId<string>, IHasVersion<long>
{
    private DateTime _createdAt;

    public DbChat() { }
    public DbChat(Chat chat)
    {
        Title = chat.Title;
        CreatedAt = chat.CreatedAt;
        IsPublic = chat.IsPublic;
        Id = chat.Id;
        Owners = chat.OwnerIds.Select(x => new DbChatOwner() {
            ChatId = chat.Id,
            UserId = x.Value,
        }).ToList();
    }

    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }
    public string Title { get; set; } = "";
    public bool IsPublic { get; set; }

    public DateTime CreatedAt {
        get => _createdAt.DefaultKind(DateTimeKind.Utc);
        set => _createdAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public List<DbChatOwner> Owners { get; set; } = new();

    public Chat ToModel()
        => new() {
            Id = Id,
            Title = Title,
            CreatedAt = CreatedAt,
            IsPublic = IsPublic,
            OwnerIds = Owners.Select(o => (UserId)o.UserId).ToImmutableArray(),
        };

    internal class EntityConfiguration : IEntityTypeConfiguration<DbChat>
    {
        public void Configure(EntityTypeBuilder<DbChat> builder)
        {
            builder.Property(a => a.Id).ValueGeneratedOnAdd().HasValueGenerator<UlidValueGenerator>();
        }
    }
}


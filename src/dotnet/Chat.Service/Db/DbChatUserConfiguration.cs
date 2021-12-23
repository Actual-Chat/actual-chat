using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Chat.Db;

[Table("ChatUserConfiguration")]
[Index(nameof(ChatId), nameof(UserId))]
public class DbChatUserConfiguration : IHasId<string>
{
    [Key] public string Id { get; set; } = null!;
    string IHasId<string>.Id => Id;

    public string ChatId { get; set; } = null!;
    public long LocalId { get; set; }

    public long Version { get; set; }
    public string UserId { get; set; } = null!;

    public string Language { get; set; } = "";

    public static string ComposeId(string chatId, long localId)
        => $"{chatId}:{localId.ToString(CultureInfo.InvariantCulture)}";

    public ChatUserConfiguration ToModel()
        => new ChatUserConfiguration() {
            Id = Id,
            ChatId = ChatId,
            UserId = UserId,
            Language = Language
        };

    internal class EntityConfiguration : IEntityTypeConfiguration<DbChatUserConfiguration>
    {
        public void Configure(EntityTypeBuilder<DbChatUserConfiguration> builder)
        {
            builder.Property(a => a.Id).IsRequired();
            builder.Property(a => a.ChatId).IsRequired();
            builder.Property(a => a.UserId).IsRequired();
            builder.Property(a => a.Version).IsConcurrencyToken();
        }
    }
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Chat.Db;

[Table("ChatUserSettings")]
[Index(nameof(ChatId), nameof(UserId))]
public class DbChatUserSettings : IHasId<string>
{
    [Key] public string Id { get; set; } = null!;
    string IHasId<string>.Id => Id;
    public string ChatId { get; set; } = null!;
    public string UserId { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public string Language { get; set; } = "";

    public static string ComposeId(string chatId, string userId)
        => $"{chatId}:{userId}";

    public ChatUserSettings ToModel()
        => new() {
            Version = Version,
            Language = new LanguageId(Language).ValidOrDefault(),
        };

    public void UpdateFrom(ChatUserSettings model)
    {
        Version = model.Version;
        Language = model.Language;
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbChatUserSettings>
    {
        public void Configure(EntityTypeBuilder<DbChatUserSettings> builder)
        {
            builder.Property(a => a.Id).IsRequired();
            builder.Property(a => a.ChatId).IsRequired();
            builder.Property(a => a.UserId).IsRequired();
            builder.Property(a => a.Version).IsConcurrencyToken();
        }
    }
}

using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Chat.Db;

[Table("ChatOwners")]
public class DbChatOwner
{
    [Column("ChatId")]
    public string DbChatId { get; set; } = "";
    [Column("UserId")]
    public string DbUserId { get; set; } = "";

    internal class EntityConfiguration : IEntityTypeConfiguration<DbChatOwner>
    {
        public void Configure(EntityTypeBuilder<DbChatOwner> builder)
            => builder.HasKey(e => new { ChatId = e.DbChatId, UserId = e.DbUserId });
    }
}

using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Chat.Db;

[Table("ChatOwners")]
public class DbChatOwner
{
    public string ChatId { get; set; } = "";
    public string UserId { get; set; } = "";

    internal class EntityConfiguration : IEntityTypeConfiguration<DbChatOwner>
    {
        public void Configure(EntityTypeBuilder<DbChatOwner> builder)
            => builder.HasKey(e => new { e.ChatId, e.UserId });
    }
}

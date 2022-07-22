using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Chat.Db;

[Table("ChatAuthorRoles")]
[Index(nameof(ChatRoleId), nameof(ChatAuthorId), IsUnique = true)]
public class DbChatAuthorRole
{
    public string ChatAuthorId { get; set; } = "";
    public string ChatRoleId { get; set; } = "";

    internal class EntityConfiguration : IEntityTypeConfiguration<DbChatAuthorRole>
    {
        public void Configure(EntityTypeBuilder<DbChatAuthorRole> builder)
            => builder.HasKey(e => new { e.ChatAuthorId, e.ChatRoleId });
    }
}

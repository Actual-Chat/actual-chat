using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Chat.Db;

[Table("ChatAuthorRoles")]
[Index(nameof(DbChatRoleId), nameof(DbChatAuthorId), IsUnique = true)]
public class DbChatAuthorRole
{
    [Column("ChatAuthorId")]
    public string DbChatAuthorId { get; set; } = "";
    [Column("ChatRoleId")]
    public string DbChatRoleId { get; set; } = "";

    internal class EntityConfiguration : IEntityTypeConfiguration<DbChatAuthorRole>
    {
        public void Configure(EntityTypeBuilder<DbChatAuthorRole> builder)
            => builder.HasKey(e => new { ChatAuthorId = e.DbChatAuthorId, ChatRoleId = e.DbChatRoleId });
    }
}

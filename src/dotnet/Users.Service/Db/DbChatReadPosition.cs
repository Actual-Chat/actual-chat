using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Users.Db;

[Table("ChatReadPositions")]
public class DbChatReadPosition
{
    // TODO(AY): Add Id + ComposeId on migration to MySql & start use entity resolver, remove UserId & ChatId
    public string UserId { get; set; } = null!;
    public string ChatId { get; set; } = null!;
    public long EntryId { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbChatReadPosition>
    {
        public void Configure(EntityTypeBuilder<DbChatReadPosition> builder)
            => builder.HasKey(e => new { e.UserId, e.ChatId});
    }
}

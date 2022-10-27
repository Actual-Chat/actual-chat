using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Users.Db;

[Table("ChatReadPositions")]
public class DbChatReadPosition : IHasId<string>, IRequirementTarget
{
    [Key] public string Id { get; set; } = null!;
    public long ReadEntryId { get; set; }

    public static string ComposeId(string userId, string chatId)
        => $"{userId} {chatId}";

    internal class EntityConfiguration : IEntityTypeConfiguration<DbChatReadPosition>
    {
        public void Configure(EntityTypeBuilder<DbChatReadPosition> builder)
            => builder.Property(a => a.Id).IsRequired();
    }
}

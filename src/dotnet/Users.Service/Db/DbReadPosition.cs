using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Users.Db;

[Table("ReadPositions")]
public class DbReadPosition : IHasId<string>, IRequirementTarget
{
    [Key] public string Id { get; set; } = null!;
    public long ReadEntryId { get; set; }

    public static string ComposeId(UserId userId, string chatId)
        => $"{userId} {chatId}";

    internal class EntityConfiguration : IEntityTypeConfiguration<DbReadPosition>
    {
        public void Configure(EntityTypeBuilder<DbReadPosition> builder)
            => builder.Property(a => a.Id).IsRequired();
    }
}

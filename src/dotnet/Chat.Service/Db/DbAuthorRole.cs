using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Chat.Db;

[Table("ChatAuthorRoles")]
[Index(nameof(DbRoleId), nameof(DbAuthorId), IsUnique = true)]
public class DbAuthorRole: IRequirementTarget
{
    [Column("AuthorId")]
    public string DbAuthorId { get; set; } = "";
    [Column("RoleId")]
    public string DbRoleId { get; set; } = "";

    internal class EntityConfiguration : IEntityTypeConfiguration<DbAuthorRole>
    {
        public void Configure(EntityTypeBuilder<DbAuthorRole> builder)
        {
            builder.Property(a => a.DbAuthorId).IsRequired();
            builder.Property(a => a.DbRoleId).IsRequired();
            builder.HasKey(e => new { AuthorId = e.DbAuthorId, RoleId = e.DbRoleId });
        }
    }
}

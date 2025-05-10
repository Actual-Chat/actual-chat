using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Chat.Db;

[Table("AuthorRoles")]
[Index(nameof(DbRoleId), nameof(DbAuthorId), IsUnique = true)]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbAuthorRole: IRequirementTarget
{
    [Column("AuthorId")] // TODO(AY): Rename to author_id
    public string DbAuthorId { get; set; } = "";
    [Column("RoleId")] // TODO(AY): Rename to role_id
    public string DbRoleId { get; set; } = "";

    internal class EntityConfiguration : IEntityTypeConfiguration<DbAuthorRole>
    {
        public void Configure(EntityTypeBuilder<DbAuthorRole> builder)
        {
            builder.HasKey(e => new { AuthorId = e.DbAuthorId, RoleId = e.DbRoleId });
            builder.Property(a => a.DbAuthorId).IsRequired();
            builder.Property(a => a.DbRoleId).IsRequired();
        }
    }
}

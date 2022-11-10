using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stl.Versioning;

namespace ActualChat.Users.Db;

[Table("Contacts")]
[Index(nameof(OwnerUserId))]
public class DbContactOld : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public string OwnerUserId { get; set; } = "";
    public string? TargetUserId { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbContactOld>
    {
        public void Configure(EntityTypeBuilder<DbContactOld> builder)
        {
            builder.Property(a => a.Id).IsRequired();
            builder.Property(a => a.OwnerUserId).IsRequired();
        }
    }
}



using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ActualLab.Versioning;

namespace ActualChat.Users.Db;

public class DbKvasEntry : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    string IHasId<string>.Id => Key;
    [Key] public string Key { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public byte[] Value { get; set; } = null!;

    internal class EntityConfiguration : IEntityTypeConfiguration<DbKvasEntry>
    {
        public void Configure(EntityTypeBuilder<DbKvasEntry> builder)
            => builder.Property(a => a.Key).IsRequired();
    }
}

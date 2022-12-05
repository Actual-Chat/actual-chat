using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stl.Versioning;

namespace ActualChat.Users.Db;

public class DbAccount : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    [Column(TypeName = "smallint")]
    public AccountStatus Status { get; set; }

    public AccountFull ToModel(User user)
    {
        if (user.Id != Id)
            throw new ArgumentOutOfRangeException(nameof(user));

        return new (user, Version) {
            Status = Status,
        };
    }

    public void UpdateFrom(AccountFull model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id);

        Id = id;
        Version = model.Version;
        Status = model.Status;
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbAccount>
    {
        public void Configure(EntityTypeBuilder<DbAccount> builder)
            => builder.Property(a => a.Id).IsRequired();
    }
}

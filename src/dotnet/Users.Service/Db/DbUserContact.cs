using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Users.Db;

[Table("UserContacts")]
[Index(nameof(OwnerUserId))]
public class DbUserContact : IHasId<string>, IRequirementTarget
{
    string IHasId<string>.Id => Id;
    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public string OwnerUserId { get; set; } = null!;
    public string TargetUserId { get; set; } = null!;
    public string Name { get; set; } = null!;

    public static string ComposeId(string ownerUserId, string contactUserId)
        => $"{ownerUserId}:{contactUserId}";

    public UserContact ToModel()
        => new UserContact {
            Id = Id,
            Name = Name,
            OwnerUserId = OwnerUserId,
            TargetUserId = TargetUserId,
            Version = Version,
        };

    internal class EntityConfiguration : IEntityTypeConfiguration<DbUserContact>
    {
        public void Configure(EntityTypeBuilder<DbUserContact> builder)
        {
            builder.Property(a => a.Id).IsRequired();
            builder.Property(a => a.OwnerUserId).IsRequired();
            builder.Property(a => a.TargetUserId).IsRequired();
        }
    }
}



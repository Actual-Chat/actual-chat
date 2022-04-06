using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Users.Db;

[Table("UserContacts")]
[Index(nameof(OwnerUserId))]
public class DbUserContact : IHasId<string>
{
    [Key] public string Id { get; set; } = null!;
    string IHasId<string>.Id => Id;
    public string OwnerUserId { get; set; } = null!;
    public string TargetPrincipalId { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }
    public string Name { get; set; } = null!;

    public static string GetCompositeId(string ownerUserId, string contactUserId)
        => $"{ownerUserId}:{contactUserId}";

    public UserContact ToModel()
        => new UserContact {
            Id = Id,
            Name = Name,
            OwnerUserId = OwnerUserId,
            TargetPrincipalId = TargetPrincipalId,
            Version = Version
        };

    internal class EntityConfiguration : IEntityTypeConfiguration<DbUserContact>
    {
        public void Configure(EntityTypeBuilder<DbUserContact> builder)
        {
            builder.Property(a => a.Id).IsRequired();
            builder.Property(a => a.OwnerUserId).IsRequired();
            builder.Property(a => a.TargetPrincipalId).IsRequired();
        }
    }
}



using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stl.Versioning;

namespace ActualChat.Users.Db;

[Table("Contacts")]
[Index(nameof(OwnerUserId))]
public class DbContact : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public string OwnerUserId { get; set; } = "";
    public string? TargetUserId { get; set; }

    public DbContact() { }
    public DbContact(Contact contact)
        => UpdateFrom(contact);

    public static string ComposeId(string ownerUserId, string contactUserId)
        => $"{ownerUserId}:{contactUserId}";

    public Contact ToModel()
        => new() {
            Id = Id,
            OwnerUserId = OwnerUserId,
            TargetUserId = TargetUserId ?? Symbol.Empty,
            Version = Version,
        };

    public void UpdateFrom(Contact model)
    {
        Id = !model.Id.IsEmpty ? model.Id : ComposeId(model.OwnerUserId, model.TargetUserId);
        OwnerUserId = model.OwnerUserId;
        TargetUserId = model.TargetUserId.NullIfEmpty()?.Value;
        Version = model.Version;
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbContact>
    {
        public void Configure(EntityTypeBuilder<DbContact> builder)
        {
            builder.Property(a => a.Id).IsRequired();
            builder.Property(a => a.OwnerUserId).IsRequired();
        }
    }
}



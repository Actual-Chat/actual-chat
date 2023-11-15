using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stl.Versioning;

namespace ActualChat.Contacts.Db;

[Table("PlaceContacts")]
[Index(nameof(OwnerId))]
public class DbPlaceContact : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public string OwnerId { get; set; } = "";
    public string PlaceId { get; set; } = "";

    public DbPlaceContact(string ownerId, string placeId)
    {
        Id = FormatId(ownerId, placeId);
        OwnerId = ownerId;
        PlaceId = placeId;
    }

    private DbPlaceContact() { }

    internal static string FormatId(string ownerId, string placeId)
        => $"{ownerId} {placeId}";

    internal class EntityConfiguration : IEntityTypeConfiguration<DbPlaceContact>
    {
        public void Configure(EntityTypeBuilder<DbPlaceContact> builder)
        {
            builder.Property(a => a.Id).IsRequired();
            builder.Property(a => a.OwnerId).IsRequired();
            builder.Property(a => a.PlaceId).IsRequired();
        }
    }
}



using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ActualLab.Versioning;

namespace ActualChat.Contacts.Db;

[Table("PlaceContacts")]
[Index(nameof(OwnerId))]
[Index(nameof(Version), nameof(Id))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbPlaceContact : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private const char IdDelimiter = ' ';
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

    public Contact ToModel()
    {
        var ownerId = UserId.Parse(OwnerId);
        var placeId = ActualChat.PlaceId.Parse(PlaceId);
        return new(ContactId.NewPlace(ownerId, placeId), Version) {
            SystemTag = Constants.Place.ChatRouletteId.Value.Equals(PlaceId)
                ? Constants.Contact.SystemTags.ChatRoulette
                : Symbol.Empty,
        };
    }

    internal static string FormatId(ContactId contactId)
    {
        var placeId = ((PlaceChatId)contactId.ChatId).PlaceId;
        return FormatId(contactId.OwnerId.Value, placeId.Value);
    }

    internal static string FormatId(string ownerId, string placeId)
        => $"{ownerId}{IdDelimiter}{placeId}";

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

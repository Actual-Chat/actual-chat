using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ActualLab.Versioning;

namespace ActualChat.Contacts.Db;

[Table("PlaceContacts")]
[Index(nameof(OwnerId))]
[Index(nameof(Version), nameof(Id))]
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

    // NOTE: we use Contact model just because it's used in very specific cases on backend. Otherwise, needs a separate model
    public Contact ToModel()
        => new(new ContactId(new UserId(OwnerId), new PlaceId(PlaceId).ToRootChatId()), Version) {
            SystemTag = Constants.Place.ChatRouletteId.Value.Equals(PlaceId) ? Constants.Contact.SystemTags.ChatRoulette : Symbol.Empty,
        };

    internal static string FormatId(ContactId contactId)
        => FormatId(contactId.OwnerId, contactId.ChatId.PlaceId);

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

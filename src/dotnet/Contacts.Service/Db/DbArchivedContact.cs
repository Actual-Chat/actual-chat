using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ActualLab.Versioning;

namespace ActualChat.Contacts.Db;

// A contact moved out of the contact list. It keeps everything the contact had, so unarchiving restores it.
// Blocking lives here too: only an archived contact can be blocked.
[Table("ArchivedContacts")]
[Index(nameof(OwnerId), nameof(PlaceId), nameof(ArchivedAt))]
[Index(nameof(OwnerId), nameof(IsBlocked))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbArchivedContact : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [DbKey] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public string OwnerId { get; set; } = "";
    public string? UserId { get; set; }
    public string ChatId { get; set; } = "";
    public string? PlaceId { get; set; }
    public string PeerContactName { get; set; } = "";
    public string ExternalContactName { get; set; } = "";
    public ContactState State { get; set; }
    public bool IsPinned { get; set; }
    public bool IsBlocked { get; set; }

    public DateTime TouchedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime ArchivedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DbArchivedContact() { }

    public DbArchivedContact(Contact contact, Moment archivedAt, long version)
    {
        Id = contact.Id.Value;
        OwnerId = contact.OwnerId.Value;
        UserId = contact.UserId?.Value;
        ChatId = contact.ChatId.Value;
        PlaceId = (contact.ChatId as PlaceChatId)?.PlaceId.Value;
        Version = version;
        PeerContactName = contact.PeerContactName;
        ExternalContactName = contact.ExternalContactName;
        // A blocked contact is stored as Regular + IsBlocked, so unblocking has a state to fall back to
        State = contact.IsBlocked ? ContactState.Regular : contact.State;
        IsPinned = contact.IsPinned;
        IsBlocked = contact.IsBlocked;
        TouchedAt = contact.TouchedAt.ToDateTimeClamped();
        ArchivedAt = archivedAt.ToDateTimeClamped();
    }

    public Contact ToModel()
        => new(ContactId.Parse(Id), Version) {
            UserId = ActualChat.UserId.ParseNullable(UserId ?? ""),
            TouchedAt = TouchedAt.ToMoment(),
            IsPinned = IsPinned,
            PeerContactName = PeerContactName,
            ExternalContactName = ExternalContactName,
            State = IsBlocked ? ContactState.Blocked : State,
            ArchivedAt = ArchivedAt.ToMoment(),
        };

    internal class EntityConfiguration : IEntityTypeConfiguration<DbArchivedContact>
    {
        public void Configure(EntityTypeBuilder<DbArchivedContact> builder)
        {
            builder.Property(a => a.Id).IsRequired();
            builder.Property(a => a.OwnerId).IsRequired();
            builder.Property(a => a.ChatId).IsRequired();
        }
    }
}

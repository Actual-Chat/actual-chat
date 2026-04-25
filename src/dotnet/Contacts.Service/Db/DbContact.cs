using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ActualLab.Versioning;

namespace ActualChat.Contacts.Db;

[Table("Contacts")]
[Index(nameof(OwnerId))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbContact : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [DbKey] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public string OwnerId { get; set; } = "";
    public string? UserId { get; set; }
    public string? ChatId { get; set; }
    public string? PlaceId { get; set; }
    public string PeerContactName { get; set; } = "";
    public string ExternalContactName { get; set; } = "";
    public ContactState State { get; set; }
    public bool IsPinned { get; set; }
    public bool IsBanned { get; set; }

    public DateTime TouchedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DbContact() { }
    public DbContact(Contact contact) => UpdateFrom(contact);

    public Contact ToModel()
        => new(ContactId.Parse(Id), Version) {
            UserId = ActualChat.UserId.ParseNullable(UserId ?? ""),
            TouchedAt = TouchedAt.ToMoment(),
            IsPinned = IsPinned,
            PeerContactName = PeerContactName,
            ExternalContactName = ExternalContactName,
            State = State,
            IsBanned = IsBanned,
        };

    public void UpdateFrom(Contact model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id.Value);
        model.RequireVersion();

        Version = model.Version;
        TouchedAt = model.TouchedAt.ToDateTimeClamped();
        IsPinned = model.IsPinned;
        PeerContactName = model.PeerContactName;
        ExternalContactName = model.ExternalContactName;
        State = model.State;
        IsBanned = model.IsBanned;
        if (!Id.IsNullOrEmpty())
            return; // Only the above properties can be changed for already existing contacts

        Id = id.Value;
        OwnerId = model.OwnerId.Value.NullIfEmpty() ?? throw StandardError.Constraint("OwnerId cannot be empty.");
        ChatId = model.ChatId.Value;
        UserId = model.UserId?.Value;
        var placeId = (model.ChatId as PlaceChatId)?.PlaceId;
        PlaceId = placeId?.Value;
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbContact>
    {
        public void Configure(EntityTypeBuilder<DbContact> builder)
        {
            builder.Property(a => a.Id).IsRequired();
            builder.Property(a => a.OwnerId).IsRequired();
            builder.HasIndex(nameof(Version), nameof(Id)).HasFilter("user_id IS NOT NULL");
        }
    }
}

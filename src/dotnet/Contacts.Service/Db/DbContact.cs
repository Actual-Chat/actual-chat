using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stl.Versioning;

namespace ActualChat.Contacts.Db;

[Table("Contacts")]
[Index(nameof(OwnerId))]
public class DbContact : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private DateTime _touchedAt;

    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public string OwnerId { get; set; } = "";
    public string? UserId { get; set; }
    public string? ChatId { get; set; }
    public bool IsPinned { get; set; }

    public DateTime TouchedAt {
        get => _touchedAt.DefaultKind(DateTimeKind.Utc);
        set => _touchedAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public DbContact() { }
    public DbContact(Contact contact) => UpdateFrom(contact);

    public Contact ToModel()
        => new(new ContactId(Id), Version) {
            UserId = new UserId(UserId ?? ""),
            TouchedAt = TouchedAt.ToMoment(),
            IsPinned = IsPinned,
        };

    public void UpdateFrom(Contact model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id.Value);
        model.RequireSomeVersion();

        Version = model.Version;
        TouchedAt = model.TouchedAt.ToDateTimeClamped();
        if (!Id.IsNullOrEmpty())
            return; // Only Version & TouchedAt can be changed for already existing contacts

        Id = id.Value;
        OwnerId = model.OwnerId.Value;
        ChatId = model.ChatId.Value.NullIfEmpty();
        UserId = model.UserId.Value.NullIfEmpty();
        IsPinned = model.IsPinned;
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbContact>
    {
        public void Configure(EntityTypeBuilder<DbContact> builder)
        {
            builder.Property(a => a.Id).IsRequired();
            builder.Property(a => a.OwnerId).IsRequired();
        }
    }
}



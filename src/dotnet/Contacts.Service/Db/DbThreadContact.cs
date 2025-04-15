using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ActualLab.Versioning;

namespace ActualChat.Contacts.Db;

[Table("ThreadContacts")]
[Index(nameof(OwnerId))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbThreadContact : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public string OwnerId { get; set; } = "";
    public string ThreadChatId { get; set; }
    public string ParentChatId { get; set; }
    public string OutermostParentChatId { get; set; }
    public string PlaceId { get; set; }
    public bool IsPinned { get; set; }

    public DateTime TouchedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DbThreadContact() { }
    public DbThreadContact(ThreadContact contact) => UpdateFrom(contact);

    public ThreadContact ToModel()
        => new(new ContactId(Id), Version) {
            TouchedAt = TouchedAt.ToMoment(),
            IsPinned = IsPinned,
        };

    public void UpdateFrom(ThreadContact model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id);
        model.RequireSomeVersion();

        Version = model.Version;
        TouchedAt = model.TouchedAt.ToDateTimeClamped();
        IsPinned = model.IsPinned;
        if (!Id.IsNullOrEmpty())
            return; // Only the above properties can be changed for already existing contacts

        Id = id;
        OwnerId = model.OwnerId.Value.NullIfEmpty() ?? throw StandardError.Constraint("OwnerId cannot be empty.");
        ThreadChatId = model.ThreadChatId;
        ParentChatId = model.ThreadChatId.Parent;
        var outermostParentChatId = model.ThreadChatId.Parent;
        OutermostParentChatId = outermostParentChatId;
        PlaceId = outermostParentChatId.PlaceId;
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbThreadContact>
    {
        public void Configure(EntityTypeBuilder<DbThreadContact> builder)
        {
            builder.Property(a => a.Id).IsRequired();
            builder.Property(a => a.OwnerId).IsRequired();
        }
    }
}

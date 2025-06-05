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
    public string ThreadChatId { get; set; } = "";
    public string ParentChatId { get; set; } = "";
    public string OutermostParentChatId { get; set; } = "";
    public string PlaceId { get; set; } = "";
    public bool IsPinned { get; set; }

    public DateTime TouchedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DbThreadContact() { }
    public DbThreadContact(ThreadContact contact) => UpdateFrom(contact);

    public ThreadContact ToModel()
        => new(ContactId.Parse(Id), Version) {
            TouchedAt = TouchedAt.ToMoment(),
            IsPinned = IsPinned,
        };

    public void UpdateFrom(ThreadContact model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id.Value);
        model.RequireSomeVersion();

        Version = model.Version;
        TouchedAt = model.TouchedAt.ToDateTimeClamped();
        IsPinned = model.IsPinned;
        if (!Id.IsNullOrEmpty())
            return; // Only the above properties can be changed for already existing contacts

        Id = id.Value;
        OwnerId = model.OwnerId.Value.NullIfEmpty() ?? throw StandardError.Constraint("OwnerId cannot be empty.");
        ThreadChatId = model.ThreadChatId.Value;
        ParentChatId = model.ThreadChatId.ParentChatId.Value;
        var outermostParentChatId = model.ThreadChatId.GetOutermostParent();
        OutermostParentChatId = outermostParentChatId.Value;
        PlaceId = (outermostParentChatId as PlaceChatId)?.PlaceId.Value ?? "";
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

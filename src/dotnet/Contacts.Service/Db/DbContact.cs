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
    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public string OwnerId { get; set; } = "";
    public string? UserId { get; set; }
    public string? ChatId { get; set; }

    public DbContact() { }
    public DbContact(Contact contact)
        => UpdateFrom(contact);

    public static string ComposeUserContactId(string ownerUserId, string contactUserId)
        => $"{ownerUserId} u/{contactUserId}";
    public static string ComposeChatContactId(string ownerUserId, string chatId)
        => $"{ownerUserId} c/{chatId}";

    public Contact ToModel()
        => new() {
            Id = Id,
            OwnerId = OwnerId,
            UserId = UserId ?? Symbol.Empty,
            ChatId = ChatId ?? Symbol.Empty,
            Version = Version,
        };

    public void UpdateFrom(Contact model)
    {
        Id = !model.Id.IsEmpty ? model.Id : ComposeUserContactId(model.OwnerId, model.UserId);
        OwnerId = model.OwnerId;
        UserId = model.UserId.NullIfEmpty()?.Value;
        ChatId = model.ChatId.NullIfEmpty()?.Value;
        Version = model.Version;
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



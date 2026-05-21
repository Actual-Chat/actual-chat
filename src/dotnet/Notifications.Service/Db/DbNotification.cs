using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Versioning;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Notifications.Db;

[Table("Notifications")]
[Index(nameof(UserId), nameof(Version))]
[Index(nameof(UserId), nameof(Id))]
[Index(nameof(UserId), nameof(Kind), nameof(SimilarityKey))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbNotification : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private DateTime? _handledAt;

    [DbKey] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; } = 0;
    public string UserId { get; set; } = null!;
    public NotificationKind Kind { get; set; }
    public string SimilarityKey { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string Content { get; set; } = null!;
    public string? ChatId { get; set; }
    public long? ChatEntryLid { get; set; }
    public string? AuthorId { get; set; }
    public string IconUrl { get; set; } = null!;
    [NotMapped] public bool IsActive => _handledAt == null;

    public DateTime CreatedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime SentAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime? HandledAt {
        get => _handledAt?.DefaultKind(DateTimeKind.Utc);
        set => _handledAt = value?.DefaultKind(DateTimeKind.Utc);
    }

    public NotificationItem ToModel()
    {
        var id = NotificationId.Parse(Id);
        var chatId = ActualChat.ChatId.Parse(ChatId);
        var entryLid = ChatEntryLid ?? 0;
        var authorId = ActualChat.AuthorId.ParseNullable(AuthorId);

        return NotificationItem.New(id, chatId, entryLid, authorId) with {
            Version = Version,
            Title = Title,
            Text = Content,
            IconUrl = IconUrl,
            CreatedAt = CreatedAt,
            SentAt = SentAt,
            HandledAt = HandledAt.ToMoment(),
        };
    }

    public void UpdateFrom(NotificationItem model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id.Value);
        model.RequireVersion();

        long? chatEntryLid = null;
        string? authorSid = null;
        ChatId? chatId = null;
        if (model is ChatNotificationItem chatModel) {
            chatId = chatModel.ChatId;
            chatEntryLid = chatModel.EntryLid;
            authorSid = chatModel.AuthorId?.Value.NullIfEmpty();
        }

        Id = id.Value;
        Version = model.Version;
        UserId = model.UserId.Value;
        Kind = model.Kind;
        SimilarityKey = model.SimilarityKey;
        Title = model.Title;
        Content = model.Text;
        IconUrl = model.IconUrl;
        ChatId = chatId?.Value.NullIfEmpty();
        ChatEntryLid = chatEntryLid;
        AuthorId = authorSid;
        CreatedAt = model.CreatedAt;
        SentAt = model.SentAt;
        HandledAt = model.HandledAt;
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbNotification>
    {
        public void Configure(EntityTypeBuilder<DbNotification> builder)
            => builder.HasAnnotation(nameof(ConflictStrategy), ConflictStrategy.DoNothing);
    }
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using ActualLab.Versioning;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Notification.Db;

[Table("Notifications")]
[Index(nameof(UserId), nameof(Version))]
[Index(nameof(UserId), nameof(Id))]
[Index(nameof(UserId), nameof(Kind), nameof(SimilarityKey))]
public class DbNotification : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private DateTime _createdAt;
    private DateTime _sentAt;
    private DateTime? _handledAt;

    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; } = 0;
    public string UserId { get; set; } = null!;
    public NotificationKind Kind { get; set; }
    public string SimilarityKey { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string Content { get; set; } = null!;
    public string? ChatId { get; set; }
    public long? TextEntryLocalId { get; set; }
    public string? AuthorId { get; set; }
    public string IconUrl { get; set; } = null!;
    [NotMapped] public bool IsActive => _handledAt == null;

    public DateTime CreatedAt {
        get => _createdAt.DefaultKind(DateTimeKind.Utc);
        set => _createdAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime SentAt {
        get => _sentAt.DefaultKind(DateTimeKind.Utc);
        set => _sentAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime? HandledAt {
        get => _handledAt?.DefaultKind(DateTimeKind.Utc);
        set => _handledAt = value?.DefaultKind(DateTimeKind.Utc);
    }

    public Notification ToModel()
    {
        var chatId = new ChatId(ChatId, ParseOrNone.Option);
        var entryId = TextEntryLocalId is { } localId && !chatId.IsNone
            ? new ChatEntryId(chatId, ChatEntryKind.Text, localId, AssumeValid.Option)
            : default;
        var authorId = new AuthorId(AuthorId, ParseOrNone.Option);

        return new Notification(new NotificationId(Id), Version) {
            Title = Title,
            Content = Content,
            IconUrl = IconUrl,
            CreatedAt = CreatedAt,
            SentAt = SentAt,
            HandledAt = HandledAt,
            Option = Kind switch {
                NotificationKind.Invitation => new ChatNotificationOption(chatId),
                NotificationKind.Message => new ChatEntryNotificationOption(entryId, authorId),
                NotificationKind.Reply => new ChatEntryNotificationOption(entryId, authorId),
                NotificationKind.Reaction => new ChatEntryNotificationOption(entryId, authorId),
                _ => throw new ArgumentOutOfRangeException(),
            },
        };
    }

    public void UpdateFrom(Notification model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id);
        model.RequireSomeVersion();

        var chatEntryNotification = model.ChatEntryNotification;
        if (chatEntryNotification != null) {
            if (chatEntryNotification.EntryId.Kind != ChatEntryKind.Text)
                throw new ArgumentOutOfRangeException(nameof(model), "EntryId must be a Text entry Id here.");
        }

        Id = id;
        Version = model.Version;
        UserId = model.UserId;
        Kind = model.Kind;
        SimilarityKey = model.SimilarityKey;
        Title = model.Title;
        Content = model.Content;
        IconUrl = model.IconUrl;
        ChatId = model.ChatId;
        TextEntryLocalId = chatEntryNotification?.EntryId.LocalId;
        AuthorId = chatEntryNotification?.AuthorId.Value.NullIfEmpty();
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

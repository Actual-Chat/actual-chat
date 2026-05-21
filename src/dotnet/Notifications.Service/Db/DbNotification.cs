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

    public Notification ToModel()
    {
        var chatId = ActualChat.ChatId.ParseNullable(ChatId);
        var entryId = ChatEntryLid is { } localId && chatId is not null
            ? ChatEntryId.New(chatId, localId)
            : default;
        var authorId = ActualChat.AuthorId.ParseNullable(AuthorId);

        return new Notification(NotificationId.Parse(Id), Version) {
            Title = Title,
            Content = Content,
            IconUrl = IconUrl,
            CreatedAt = CreatedAt,
            SentAt = SentAt,
            HandledAt = HandledAt.ToMoment(),
            Option = Kind switch {
                NotificationKind.Invitation
                    => new ChatNotificationOption(chatId!),
                NotificationKind.Message or
                NotificationKind.Reply or
                NotificationKind.Reaction or
                NotificationKind.NewThread
                    => new ChatEntryNotificationOption(entryId!, authorId!),
                NotificationKind.Attention
                    => new GetAttentionNotificationOption(chatId!, authorId!, entryId?.LocalId ?? 0),
                _ => throw new ArgumentOutOfRangeException(),
            },
        };
    }

    public void UpdateFrom(Notification model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id.Value);
        model.RequireVersion();

        long? textEntryLocalId = null;
        string? authorSid = null;
        var chatEntryNotification = model.ChatEntryNotification;
        if (chatEntryNotification != null) {
            textEntryLocalId = chatEntryNotification.EntryId.LocalId;
            authorSid = chatEntryNotification.AuthorId.Value.NullIfEmpty();
        }
        var getAttentionNotification = model.GetAttentionNotification;
        if (getAttentionNotification != null) {
            textEntryLocalId = getAttentionNotification.LastEntryLocalId;
            authorSid = getAttentionNotification.CallerId.Value.NullIfEmpty();
        }

        Id = id.Value;
        Version = model.Version;
        UserId = model.UserId.Value;
        Kind = model.Kind;
        SimilarityKey = model.SimilarityKey;
        Title = model.Title;
        Content = model.Content;
        IconUrl = model.IconUrl;
        ChatId = model.ChatId?.Value;
        ChatEntryLid = textEntryLocalId;
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

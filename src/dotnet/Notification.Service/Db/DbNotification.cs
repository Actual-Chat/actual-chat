using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Stl.Versioning;

namespace ActualChat.Notification.Db;

[Table("Notifications")]
[Index(nameof(UserId), nameof(Id))]
public class DbNotification : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private DateTime _createdAt;
    private DateTime? _handledAt;

    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; } = 0;
    public string UserId { get; set; } = null!;
    public NotificationKind Kind { get; set; }
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

    public DateTime? HandledAt {
        get => _handledAt?.DefaultKind(DateTimeKind.Utc);
        set => _handledAt = value?.DefaultKind(DateTimeKind.Utc);
    }

    public static Symbol ComposeId(UserId userId, Ulid localId)
        => $"{userId} {localId.ToString()}";

    public Notification ToModel()
    {
        var chatId = new ChatId(ChatId, ParseOrNone.Option);
        var entryId = TextEntryLocalId is { } localId && !chatId.IsNone
            ? new ChatEntryId(chatId, ChatEntryKind.Text, localId, AssumeValid.Option)
            : default;
        var authorId = new AuthorId(AuthorId, ParseOrNone.Option);

        return new Notification(new NotificationId(Id), Version) {
            UserId = new UserId(UserId, ParseOrNone.Option),
            Kind = Kind,
            Title = Title,
            Content = Content,
            IconUrl = IconUrl,
            CreatedAt = CreatedAt,
            HandledAt = HandledAt,
            ChatEntryNotification = Kind switch {
                NotificationKind.Invitation => null,
                NotificationKind.Message => new ChatEntryNotification(entryId, authorId),
                NotificationKind.Reply => new ChatEntryNotification(entryId, authorId),
                NotificationKind.Reaction => new ChatEntryNotification(entryId, authorId),
                _ => throw new ArgumentOutOfRangeException(),
            },
            ChatNotification = Kind switch {
                NotificationKind.Invitation => new ChatNotification(chatId),
                NotificationKind.Message => null,
                NotificationKind.Reply => null,
                NotificationKind.Reaction => null,
                _ => throw new ArgumentOutOfRangeException(),
            },
        };
    }

    public void UpdateFrom(Notification model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id);
        model.RequireVersion();

        var chatEntryNotification = model.ChatEntryNotification;
        if (chatEntryNotification != null) {
            if (chatEntryNotification.EntryId.EntryKind != ChatEntryKind.Text)
                throw new ArgumentOutOfRangeException(nameof(model), "EntryId must be a Text entry Id here.");
        }

        Id = id;
        Version = model.Version;
        UserId = model.UserId;
        Kind = model.Kind;
        Title = model.Title;
        Content = model.Content;
        IconUrl = model.IconUrl;
        ChatId = model.ChatId;
        TextEntryLocalId = chatEntryNotification?.EntryId.LocalId;
        AuthorId = chatEntryNotification?.AuthorId;
        CreatedAt = model.CreatedAt;
        HandledAt = model.HandledAt;
    }
}

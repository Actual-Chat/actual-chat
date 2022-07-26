using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Notification.Db;

[Table("Notifications")]
public class DbNotification : IHasId<string>
{
    private DateTime _createdAt;
    private DateTime? _modifiedAt;
    private DateTime? _handledAt;

    [Key] public string Id { get; set; } = null!;

    public string UserId { get; set; } = null!;

    public NotificationType NotificationType { get; set; }

    public string Title { get; set; } = null!;

    public string Content { get; set; } = null!;

    public string? ChatId { get; set; }

    public long? ChatEntryId { get; set; }

    public string? ChatAuthorId { get; set; }

    [NotMapped] public bool IsActive => _handledAt != null;

    public DateTime CreatedAt {
        get => _createdAt.DefaultKind(DateTimeKind.Utc);
        set => _createdAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime? ModifiedAt {
        get => _modifiedAt?.DefaultKind(DateTimeKind.Utc);
        set => _modifiedAt = value?.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime? HandledAt {
        get => _handledAt?.DefaultKind(DateTimeKind.Utc);
        set => _handledAt = value?.DefaultKind(DateTimeKind.Utc);
    }
}

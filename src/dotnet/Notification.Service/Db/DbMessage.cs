using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Notification.Db;

[Table("Messages")]
[Index(nameof(DeviceId))]
public class DbMessage : IHasId<string>
{
    private DateTime _createdAt;
    private DateTime? _accessedAt;

    [Key] public string Id { get; set; } = null!;
    public string DeviceId { get; set; } = null!;
    public string? ChatId { get; set; }
    public long? ChatEntryId { get; set; }

    public DateTime CreatedAt {
        get => _createdAt.DefaultKind(DateTimeKind.Utc);
        set => _createdAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime? AccessedAt {
        get => _accessedAt?.DefaultKind(DateTimeKind.Utc);
        set => _accessedAt = value?.DefaultKind(DateTimeKind.Utc) ?? default;
    }
}

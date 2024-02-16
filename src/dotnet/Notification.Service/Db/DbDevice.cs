using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using ActualLab.Versioning;

namespace ActualChat.Notification.Db;

[Table("Devices")]
[Index(nameof(UserId))]
public class DbDevice : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private DateTime _createdAt;
    private DateTime _accessedAt;

    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public string UserId { get; set; } = null!;
    public DeviceType Type { get; set; }

    public DateTime CreatedAt {
        get => _createdAt.DefaultKind(DateTimeKind.Utc);
        set => _createdAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime? AccessedAt {
        get => _accessedAt.DefaultKind(DateTimeKind.Utc);
        set => _accessedAt = value?.DefaultKind(DateTimeKind.Utc) ?? default;
    }

    public Device ToModel()
        => new (Id, Type, CreatedAt, AccessedAt);
}

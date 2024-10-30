using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using ActualLab.Versioning;

namespace ActualChat.Notification.Db;

[Table("ManualNotifications")]
[Index(nameof(UserId), nameof(Version))]
[Index(nameof(UserId), nameof(Id))]
[Index(nameof(UserId), nameof(Kind), nameof(SimilarityKey))]
public class DbManualNotification : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private DateTime _createdAt;
    private DateTime _updatedAt;

    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }
    public string UserId { get; set; } = null!;
    public ManualNotificationKind Kind { get; set; }
    public string SimilarityKey { get; set; } = null!;

    public DateTime CreatedAt {
        get => _createdAt.DefaultKind(DateTimeKind.Utc);
        set => _createdAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime UpdatedAt {
        get => _updatedAt.DefaultKind(DateTimeKind.Utc);
        set => _updatedAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public ManualNotification ToModel()
        => new (new ManualNotificationId(Id), Version) {
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
        };

    public void UpdateFrom(ManualNotification model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id);
        model.RequireSomeVersion();

        Id = id;
        Version = model.Version;
        UserId = model.UserId;
        Kind = model.Kind;
        SimilarityKey = model.SimilarityKey;
        CreatedAt = model.CreatedAt;
        UpdatedAt = model.UpdatedAt;
    }
}



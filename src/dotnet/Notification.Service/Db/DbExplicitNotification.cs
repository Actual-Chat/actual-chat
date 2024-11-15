using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using ActualLab.Versioning;

namespace ActualChat.Notification.Db;

[Table("ManualNotifications")]
[Index(nameof(UserId), nameof(Version))]
[Index(nameof(UserId), nameof(Id))]
[Index(nameof(UserId), nameof(Kind), nameof(SimilarityKey))]
public class DbExplicitNotification : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [Key] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }
    public string UserId { get; set; } = null!;
    public ExplicitNotificationKind Kind { get; set; }
    public string SimilarityKey { get; set; } = null!;

    public DateTime CreatedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime UpdatedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public ExplicitNotification ToModel()
        => new (new ExplicitNotificationId(Id), Version) {
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
        };

    public void UpdateFrom(ExplicitNotification model)
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

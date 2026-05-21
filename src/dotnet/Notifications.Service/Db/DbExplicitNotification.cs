using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using ActualLab.Versioning;

namespace ActualChat.Notifications.Db;

[Table("ExplicitNotifications")]
[Index(nameof(UserId), nameof(Version))]
[Index(nameof(UserId), nameof(Id))]
[Index(nameof(UserId), nameof(Kind), nameof(SimilarityKey))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbExplicitNotification : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [DbKey] public string Id { get; set; } = null!;
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
        => new (ExplicitNotificationId.Parse(Id), Version) {
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
        };

    public void UpdateFrom(ExplicitNotification model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id.Value);
        model.RequireVersion();

        Id = id.Value;
        Version = model.Version;
        UserId = model.UserId.Value;
        Kind = model.Kind;
        SimilarityKey = model.SimilarityKey;
        CreatedAt = model.CreatedAt;
        UpdatedAt = model.UpdatedAt;
    }
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using ActualLab.Versioning;

namespace ActualChat.Notifications.Db;

[Table("Devices")]
[Index(nameof(UserId))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbDevice : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [DbKey] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public string UserId { get; set; } = null!;
    public string SessionHash { get; set; } = null!;
    public DeviceType Type { get; set; }
    public bool IsPttEnabled { get; set; }

    public DateTime CreatedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime? AccessedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value?.DefaultKind(DateTimeKind.Utc) ?? default;
    }

    public Device ToModel()
        => new (Id, Type, CreatedAt) {
            AccessedAt = AccessedAt.ToMoment(),
            IsPttEnabled = IsPttEnabled,
        };
}

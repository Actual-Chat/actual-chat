using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualLab.Versioning;

namespace ActualChat.Notification.Db;

// One row per user; Data is the serialized UserNotificationInfo blob.
[Table("UserNotifications")]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbUserNotifications : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [DbKey] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }
    public byte[] Data { get; set; } = [];
    public bool HasUnsentDelta { get; set; }
    public bool IsDormant { get; set; }
}

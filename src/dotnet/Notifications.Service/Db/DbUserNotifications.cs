using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualChat.Db;
using ActualLab.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Notifications.Db;

// One row per user. ItemsData holds the whole serialized UserNotificationInfo - PendingDismissals,
// LastPushAt and IsDormant ride along with Items, since a removal and the dismissal it owes have to
// be one write.
[Table("UserNotifications")]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbUserNotifications : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    // Format version 0 = MessagePack; no legacy fallback (table is new in feat/notif-api).
    private static readonly IByteSerializer Serializer = new VersionedByteSerializer([Serializers.MessagePack]);

    [DbKey] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }
    public byte[] ItemsData { get; set; } = [];
    public bool IsDormant { get; set; }

    public UserNotificationInfo ToModel()
    {
        var info = (UserNotificationInfo)Serializer.Read(ItemsData, typeof(UserNotificationInfo), out _)!;
        return info with { Version = Version };
    }

    public void UpdateFrom(UserNotificationInfo model)
    {
        this.RequireSameOrEmptyId(model.UserId.Value);
        model.RequireVersion();

        using var buffer = Serializer.Write(model);
        Id = model.UserId.Value;
        Version = model.Version;
        ItemsData = buffer.ToArray();
        IsDormant = model.IsDormant;
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbUserNotifications>
    {
        public void Configure(EntityTypeBuilder<DbUserNotifications> builder)
            => builder.HasAnnotation(nameof(ConflictStrategy), ConflictStrategy.DoNothing);
    }
}

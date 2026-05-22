using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualChat.Db;
using ActualChat.Serialization;
using ActualLab.Serialization;
using ActualLab.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Notifications.Db;

// One row per user; Data is the serialized UserNotificationInfo blob.
[Table("UserNotifications")]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbUserNotifications : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    // Format version 0 = MessagePack; no legacy fallback (table is new in feat/notif-api).
    private static readonly IByteSerializer Serializer = new VersionedByteSerializer([Serializers.MessagePack]);

    [DbKey] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }
    public byte[] Data { get; set; } = [];
    public bool HasUnsentDelta { get; set; }
    public bool IsDormant { get; set; }

    public UserNotificationInfo ToModel()
    {
        var info = (UserNotificationInfo)Serializer.Read(Data, typeof(UserNotificationInfo), out _)!;
        return info with { Version = Version };
    }

    public void UpdateFrom(UserNotificationInfo model)
    {
        this.RequireSameOrEmptyId(model.UserId.Value);
        model.RequireVersion();

        using var buffer = Serializer.Write(model);
        Id = model.UserId.Value;
        Version = model.Version;
        Data = buffer.ToArray();
        HasUnsentDelta = !model.UnsentDelta.IsEmpty;
        IsDormant = model.IsDormant;
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbUserNotifications>
    {
        public void Configure(EntityTypeBuilder<DbUserNotifications> builder)
            => builder.HasAnnotation(nameof(ConflictStrategy), ConflictStrategy.DoNothing);
    }
}

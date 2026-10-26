using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users.Db;

[Table("UserMilestoneDeliveries")]
[Index(nameof(MilestoneId), nameof(Target), IsUnique = true)]
[Index(nameof(UserId))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public sealed class DbUserMilestoneDelivery
{
    public long Id { get; set; }
    public long MilestoneId { get; set; }
    public string UserId { get; set; } = "";
    public MilestoneTarget Target { get; set; }
    public MilestoneDeliveryState State { get; set; }
    public long? AttributionId { get; set; }
    public int Attempts { get; set; }
    public string Note { get; set; } = "";

    public DateTime UpdatedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime? NextAttemptAt {
        get => field?.DefaultKind(DateTimeKind.Utc);
        set => field = value?.DefaultKind(DateTimeKind.Utc);
    }

    public void UpdateFrom(MilestoneDelivery delivery)
    {
        MilestoneId = delivery.MilestoneId;
        Target = delivery.Target;
        State = delivery.State;
        AttributionId = delivery.AttributionId;
        Attempts = delivery.Attempts;
        Note = delivery.Note;
        UpdatedAt = delivery.UpdatedAt.ToDateTimeClamped();
        NextAttemptAt = delivery.NextAttemptAt?.ToDateTimeClamped();
    }

    public MilestoneDelivery ToModel()
        => new(MilestoneId, Target, State, UpdatedAt.ToMoment()) {
            AttributionId = AttributionId,
            Attempts = Attempts,
            NextAttemptAt = NextAttemptAt?.ToMoment(),
            Note = Note,
        };
}

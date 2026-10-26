using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users.Db;

[Table("UserMilestones")]
[Index(nameof(UserId), nameof(Kind), IsUnique = true)]
public sealed class DbUserMilestone
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public MilestoneKind Kind { get; set; }
    public long? AttributionId { get; set; }

    public DateTime ReachedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public UserMilestone ToModel()
        => new(Id, ActualChat.UserId.Parse(UserId), Kind, ReachedAt.ToMoment(), AttributionId);
}

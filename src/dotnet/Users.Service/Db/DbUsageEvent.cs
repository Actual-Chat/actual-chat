using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users.Db;

[Table("UsageEvents")]
[Index(nameof(UserId), nameof(Kind), nameof(SourceId), IsUnique = true)]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbUsageEvent
{
    public string UserId { get; set; } = "";

    public DateTime OccurredAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public UsageEventKind Kind { get; set; }
    public string SourceId { get; set; } = "";
    public long Value { get; set; }
    [Column(TypeName = "jsonb")]
    public string? Attributes { get; set; }

    public DbUsageEvent() { }
    public DbUsageEvent(UserId userId, UsageEvent model)
    {
        UserId = userId.Value;
        OccurredAt = model.OccurredAt.ToDateTimeClamped();
        Kind = model.Kind;
        SourceId = model.SourceId;
        Value = model.Value;
        Attributes = model.Attributes is { } attributes
            ? SystemJsonSerializer.Default.Write(attributes)
            : null;
    }

    public UsageEvent ToModel()
        => new(Kind, OccurredAt.ToMoment(), SourceId, Value,
            Attributes.IsNullOrEmpty() ? null : SystemJsonSerializer.Default.Read<UsageEventAttributes>(Attributes));
}

using System.ComponentModel.DataAnnotations.Schema;
using ActualChat.Chat;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users.Db;

[Table("CoachEvents")]
[Index(nameof(UserId), nameof(Day))]
[Index(nameof(UserId), nameof(OccurredAt))]
[Index(nameof(UserId), nameof(ChatId), nameof(OccurredAt))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbCoachEvent : IRequirementTarget
{
    public string UserId { get; set; } = "";
    public string SourceId { get; set; } = "";
    public CoachRecordKind Kind { get; set; }
    public string ChatId { get; set; } = "";
    public long Version { get; set; }
    // A tombstone: the row stays so a late delivery of an older version cannot bring the data back
    public bool IsRemoved { get; set; }
    // The user took this row out of every score; it is the truth, the payload copy only rides along
    public bool IsExcluded { get; set; }

    public DateTime Day {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime OccurredAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    [Column(TypeName = "jsonb")]
    public string Payload { get; set; } = "{}";
    public byte[]? PaceData { get; set; }

    public DbCoachEvent() { }
    public DbCoachEvent(CoachRecord record) => UpdateFrom(record);

    public CoachRecord ToModel()
    {
        var model = SystemJsonSerializer.Default.Read<CoachRecord>(Payload);
        return model with {
            IsExcluded = IsExcluded,
            Entry = model.Entry is { } entry
                ? entry with { Pace = PaceData is null ? null : SpeechPaceMeasurement.FromBytes(PaceData) }
                : null,
        };
    }

    public void MarkRemoved()
    {
        IsRemoved = true;
        Payload = "{}";
        PaceData = null;
    }

    public void UpdateFrom(CoachRecord record)
    {
        IsRemoved = false;
        IsExcluded = record.IsExcluded;
        Version = record.Version;
        UserId = record.UserId.Value;
        SourceId = record.SourceId;
        Kind = record.Kind;
        ChatId = record.ChatId.Value;
        Day = record.Day.ToDateTimeClamped();
        OccurredAt = record.OccurredAt.ToDateTimeClamped();
        PaceData = record.Entry?.Pace?.ToBytes();
        var payload = record.Entry?.Pace is null
            ? record
            : record with { Entry = record.Entry with { Pace = null } };
        Payload = SystemJsonSerializer.Default.Write(payload);
    }
}

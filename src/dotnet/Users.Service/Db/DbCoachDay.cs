using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualChat.Chat;
using ActualLab.Versioning;

namespace ActualChat.Users.Db;

[Table("CoachDays")]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbCoachDay : IHasVersion<long>
{
    public string UserId { get; set; } = "";
    public string Language { get; set; } = "";

    public DateTime Day {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    [ConcurrencyCheck] public long Version { get; set; }
    [Column(TypeName = "jsonb")]
    public string Data { get; set; } = "{}";
    public byte[]? PaceData { get; set; }

    public CoachDay ToModel()
        => SystemJsonSerializer.Default.Read<CoachDay>(Data) with {
            Pace = PaceData is null ? null : SpeechPaceSummary.FromBytes(PaceData),
        };

    public void UpdateFrom(CoachDay day)
    {
        Day = day.Day.ToDateTimeClamped();
        Language = day.Language;
        PaceData = day.Pace?.ToBytes();
        Data = SystemJsonSerializer.Default.Write(day with { Pace = null });
    }
}

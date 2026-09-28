using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualLab.Versioning;

namespace ActualChat.Users.Db;

[Table("CoachDays")]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbCoachDay : IHasVersion<long>
{
    public string UserId { get; set; } = "";

    public DateTime Day {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    [ConcurrencyCheck] public long Version { get; set; }
    [Column(TypeName = "jsonb")]
    public string Data { get; set; } = "{}";

    public CoachDay ToModel()
        => SystemJsonSerializer.Default.Read<CoachDay>(Data);

    public void UpdateFrom(CoachDay day)
    {
        Day = day.Day.ToDateTimeClamped();
        Data = SystemJsonSerializer.Default.Write(day);
    }
}

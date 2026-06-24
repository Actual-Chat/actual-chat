using System.ComponentModel.DataAnnotations.Schema;

namespace ActualChat.Chat.Db;

[Table("LiveLocations")]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbLiveLocation : IHasId<string>, IRequirementTarget
{
    [DbKey] public string Id { get; set; } = null!;
    public string ChatId { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public float? Accuracy { get; set; }
    public float? Bearing { get; set; }

    public DateTime CreatedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime ModifiedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public TimeSpan Duration { get; set; }
    public DbLiveLocation() { }

    public LiveLocation ToModel()
        => new(
            ActualChat.ChatId.Parse(ChatId),
            ActualChat.AuthorId.Parse(Id),
            new GeoPoint(Latitude, Longitude, Accuracy, Bearing),
            CreatedAt.ToMoment(),
            ModifiedAt.ToMoment(),
            Duration);
}

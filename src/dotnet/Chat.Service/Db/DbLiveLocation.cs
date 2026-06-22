using System.ComponentModel.DataAnnotations.Schema;

namespace ActualChat.Chat.Db;

[Table("LiveLocations")]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbLiveLocation : IHasId<string>, IRequirementTarget
{
    [DbKey] public string Id { get; set; } = null!;
    public string ChatId { get; set; } = "";
    public string AuthorId { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public float? Accuracy { get; set; }
    public float? Bearing { get; set; }

    public DateTime StartedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime UpdatedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public TimeSpan Duration { get; set; }

    public static string ComposeId(ChatId chatId, AuthorId authorId)
        => $"{chatId}:{authorId}";

    public DbLiveLocation() { }

    public LiveLocation ToModel()
        => new(
            ActualChat.ChatId.Parse(ChatId),
            ActualChat.AuthorId.Parse(AuthorId),
            new GeoPoint(Latitude, Longitude, Accuracy, Bearing),
            StartedAt.ToMoment(),
            UpdatedAt.ToMoment(),
            Duration);
}

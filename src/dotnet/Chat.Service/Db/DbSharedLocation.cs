using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualLab.Versioning;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Db;

[Table("SharedLocations")]
[Index(nameof(ChatId))]
[Index(nameof(AuthorId))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbSharedLocation : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [DbKey] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }
    public string ChatId { get; set; } = "";
    public string AuthorId { get; set; } = "";
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
    public DateTime? StoppedAt {
        get => field?.DefaultKind(DateTimeKind.Utc);
        set => field = value?.DefaultKind(DateTimeKind.Utc);
    }
    public DbSharedLocation() { }
    public DbSharedLocation(SharedLocation model) => UpdateFrom(model);

    public SharedLocation ToModel()
        => new(ActualChat.SharedLocationId.Parse(Id), Version) {
            AuthorId = ActualChat.AuthorId.Parse(AuthorId),
            Point = new GeoPoint(Latitude, Longitude, Accuracy, Bearing),
            CreatedAt = CreatedAt.ToMoment(),
            ModifiedAt = ModifiedAt.ToMoment(),
            Duration = Duration,
            StoppedAt = StoppedAt?.ToMoment(),
        };

    public void UpdateFrom(SharedLocation model)
    {
        this.RequireSameOrEmptyId(model.Id.Value);
        model.RequireVersion();
        Id = model.Id.Value;
        Version = model.Version;
        ChatId = model.ChatId.Value;
        AuthorId = model.AuthorId.Value;
        Latitude = model.Point.Latitude;
        Longitude = model.Point.Longitude;
        Accuracy = model.Point.Accuracy;
        Bearing = model.Point.Bearing;
        CreatedAt = model.CreatedAt;
        ModifiedAt = model.ModifiedAt;
        Duration = model.Duration;
        StoppedAt = model.StoppedAt?.ToDateTime();
    }
}

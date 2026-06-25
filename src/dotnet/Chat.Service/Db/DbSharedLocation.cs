using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Db;

[Table("SharedLocations")]
[Index(nameof(ChatId))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbSharedLocation : IHasId<string>, IRequirementTarget
{
    [DbKey] public string Id { get; set; } = null!;
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

    // TODO: persist CreatedAt + Duration instead
    public DateTime LiveUntil {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DbSharedLocation() { }

    public SharedLocation ToModel()
        => new(
            ActualChat.SharedLocationId.Parse(Id),
            ActualChat.ChatId.Parse(ChatId),
            ActualChat.AuthorId.Parse(AuthorId),
            new GeoPoint(Latitude, Longitude, Accuracy, Bearing),
            CreatedAt.ToMoment(),
            ModifiedAt.ToMoment(),
            LiveUntil.ToMoment());

    // TODO: UpdateFrom???
}

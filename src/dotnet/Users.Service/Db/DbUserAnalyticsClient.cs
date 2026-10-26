using System.ComponentModel.DataAnnotations.Schema;

namespace ActualChat.Users.Db;

[Table("UserAnalyticsClients")]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public sealed class DbUserAnalyticsClient
{
    public string UserId { get; set; } = "";
    public AnalyticsClientKind Kind { get; set; }
    public string ClientId { get; set; } = "";

    public DateTime LastSeenAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public UserAnalyticsClient ToModel()
        => new(ActualChat.UserId.Parse(UserId), Kind, ClientId, LastSeenAt.ToMoment());
}

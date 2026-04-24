using System.ComponentModel.DataAnnotations.Schema;

namespace ActualChat.Users.Db;

[Table("Presences")]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbUserPresence : IRequirementTarget
{
    [DbKey] public string UserId { get; set; } = "";

    public bool IsActive { get; set; }

    public DateTime CheckInAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }
}

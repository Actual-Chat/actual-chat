using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ActualChat.Users.Db;

// TODO(AY): Rename this table to UserPresences on migration to MySql
[Table("UserPresences")]
public class DbUserPresence : IRequirementTarget
{
    private DateTime _onlineCheckInAt;

    [Key] public string UserId { get; set; } = "";

    public DateTime OnlineCheckInAt {
        get => _onlineCheckInAt.DefaultKind(DateTimeKind.Utc);
        set => _onlineCheckInAt = value.DefaultKind(DateTimeKind.Utc);
    }
}

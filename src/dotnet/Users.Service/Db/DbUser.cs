using Stl.Fusion.Authentication.Services;

namespace ActualChat.Users.Db;

public class DbUser : DbUser<string>, IRequirementTarget
{
    private DateTime _createdAt = CoarseSystemClock.Now;

    public DateTime CreatedAt {
        get => _createdAt.DefaultKind(DateTimeKind.Utc);
        set => _createdAt = value.DefaultKind(DateTimeKind.Utc);
    }
}

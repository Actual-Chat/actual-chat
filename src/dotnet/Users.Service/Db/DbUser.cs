using Stl.Fusion.EntityFramework.Authentication;
using Stl.Generators;

namespace ActualChat.Users.Db;

public class DbUser : DbUser<string>, IRequirementTarget
{
    public static RandomStringGenerator IdGenerator { get; } = new (6, Alphabet.AlphaNumeric);

    private DateTime _createdAt = CoarseSystemClock.Now;

    public DateTime CreatedAt {
        get => _createdAt.DefaultKind(DateTimeKind.Utc);
        set => _createdAt = value.DefaultKind(DateTimeKind.Utc);
    }
}

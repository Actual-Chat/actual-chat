using Stl.Fusion.EntityFramework;

namespace ActualChat.Users.Db;

public class DbUserProfileConverter : DbEntityConverter<UsersDbContext, DbUserProfile, UserProfile>
{
    public DbUserProfileConverter(IServiceProvider services) : base(services)
    {
    }

    public override DbUserProfile NewEntity() => new ();

    public override UserProfile NewModel() => new ("", new User(""));

    public override void UpdateEntity(UserProfile source, DbUserProfile target)
    {
        target.Id = source.Id;
        target.Version = source.Version;
        target.Status = source.Status;
    }

    public override UserProfile UpdateModel(DbUserProfile source, UserProfile target)
        => target with {
            Id = source.Id,
            Status = source.Status,
            Version = source.Version,
        };
}

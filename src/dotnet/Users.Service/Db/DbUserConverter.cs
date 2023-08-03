using Stl.Fusion.Authentication.Services;

namespace ActualChat.Users.Db;

public sealed class DbUserConverter : DbUserConverter<UsersDbContext, DbUser, string>
{
    public DbUserConverter(IServiceProvider services) : base(services) { }

    public override void UpdateEntity(User source, DbUser target)
    {
        base.UpdateEntity(source, target);
        // remove identities
        target.Identities.RemoveAll(x => !source.Identities.ContainsKey(new (x.Id)));
    }
}

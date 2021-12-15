using Stl.Fusion.EntityFramework;

namespace ActualChat.Users.Db;

public class DbUserByNameResolver : DbEntityResolver<UsersDbContext, string, DbUser>
{
    public DbUserByNameResolver(IServiceProvider services)
        : base(new Options() { KeyPropertyName = "Name" }, services)
    { }
}

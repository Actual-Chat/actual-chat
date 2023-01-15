using ActualChat.Db;
using ActualChat.Users.Db;

namespace ActualChat.Users.Module;

public partial class UsersDbInitializer : DbInitializer<UsersDbContext>
{
    public UsersDbInitializer(IServiceProvider services) : base(services)
    { }
}

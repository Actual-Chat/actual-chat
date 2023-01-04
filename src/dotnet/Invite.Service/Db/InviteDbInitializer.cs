using ActualChat.Db;

namespace ActualChat.Invite.Db;

public class InviteDbInitializer : DbInitializer<InviteDbContext>
{
    public InviteDbInitializer(IServiceProvider services) : base(services)
    { }
}

using ActualChat.Db;

namespace ActualChat.Invite.Db;

public class InviteDbInitializer(IServiceProvider services) : DbInitializer<InviteDbContext>(services);

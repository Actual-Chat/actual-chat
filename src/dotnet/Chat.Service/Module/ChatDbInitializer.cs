using ActualChat.Chat.Db;
using ActualChat.Db;

namespace ActualChat.Chat.Module;

public partial class ChatDbInitializer : DbInitializer<ChatDbContext>
{
    public ChatDbInitializer(IServiceProvider services) : base(services)
    { }
}

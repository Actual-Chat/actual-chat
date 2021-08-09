using System;
using ActualChat.Db;
using ActualChat.Hosting;
using Stl.DependencyInjection;

namespace ActualChat.Chat.Module
{
    public class ChatDbInitializer : DbInitializer<ChatDbContext>
    {
        public ChatDbInitializer(IServiceProvider services) : base(services) { }
    }
}

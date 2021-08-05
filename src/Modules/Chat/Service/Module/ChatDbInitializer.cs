using System;
using ActualChat.Db;
using ActualChat.Hosting;
using Stl.DependencyInjection;

namespace ActualChat.Chat.Module
{
    [RegisterService(typeof(IDataInitializer), IsEnumerable = true)]
    public class ChatDbInitializer : DbInitializer<ChatDbContext>
    {
        public ChatDbInitializer(IServiceProvider services) : base(services) { }
    }
}

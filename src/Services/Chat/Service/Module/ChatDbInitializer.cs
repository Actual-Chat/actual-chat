using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ActualChat.Chat.Db;
using ActualChat.Db;
using ActualChat.Users;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion.Authentication;

namespace ActualChat.Chat.Module
{
    public class ChatDbInitializer : DbInitializer<ChatDbContext>
    {
        public ChatDbInitializer(IServiceProvider services) : base(services) { }

        public override async Task Initialize(CancellationToken cancellationToken)
        {
            await base.Initialize(cancellationToken);
            var dependencies = InitializeTasks
                .Where(kv => kv.Key.GetType().Name.StartsWith("Users"))
                .Select(kv => kv.Value)
                .ToArray();
            await Task.WhenAll(dependencies);

            if (ShouldRecreateDb) {
                var auth = Services.GetRequiredService<IServerSideAuthService>();
                var chats = Services.GetRequiredService<IServerSideChatService>();
                var session = UserConstants.AdminSession;

                // Creating "The Actual One" chat
                var chat = await chats.Create(new ChatCommands.Create(session, "The Actual One"), cancellationToken);
                ChatConstants.DefaultChatId = chat.Id;
            }
        }
    }
}

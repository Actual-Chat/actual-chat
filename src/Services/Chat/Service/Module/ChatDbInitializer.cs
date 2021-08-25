using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ActualChat.Chat.Db;
using ActualChat.Db;
using ActualChat.Users;
using Microsoft.Extensions.DependencyInjection;
using Stl.CommandR;
using Stl.Fusion.Authentication;
using Stl.Fusion.Authentication.Commands;

namespace ActualChat.Chat.Module
{
    public class ChatDbInitializer : DbInitializer<ChatDbContext>
    {
        public ChatDbInitializer(IServiceProvider services) : base(services) { }

        public override async Task Initialize(bool recreate, CancellationToken cancellationToken = default)
        {
            await base.Initialize(recreate, cancellationToken);
            if (recreate) {
                var commander = Services.Commander();
                var auth = Services.GetRequiredService<IServerSideAuthService>();
                var chats = Services.GetRequiredService<IServerSideChatService>();
                var session = Services.GetRequiredService<ISessionFactory>().CreateSession();

                // Creating admin user
                var user = new User("", "Admin").WithIdentity(new UserIdentity("internal", "admin"));
                await auth.SignIn(
                    new SignInCommand(session, user, user.Identities.Keys.Single()).MarkServerSide(),
                    cancellationToken);
                user = await auth.GetUser(session, cancellationToken);
                UserConstants.AdminId = user.Id;

                // Creating "The Actual One" chat
                var chat = await chats.Create(new ChatCommands.Create(session, "The Actual One"), cancellationToken);
                ChatConstants.DefaultChatId = chat.Id;
            }
        }
    }
}

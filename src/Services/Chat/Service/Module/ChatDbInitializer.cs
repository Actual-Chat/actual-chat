using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ActualChat.Chat.Db;
using ActualChat.Db;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion.Authentication;
using Stl.Fusion.EntityFramework;
using Stl.Fusion.EntityFramework.Authentication;
using Stl.Time;

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
                var dbContextFactory = Services.GetRequiredService<IDbContextFactory<ChatDbContext>>();

                // Creating "The Actual One" chat
                await using var dbContext = dbContextFactory.CreateDbContext().ReadWrite();
                var defaultChatId = ChatConstants.DefaultChatId;
                var adminUserId = UserConstants.AdminUserId;
                var dbChat = new DbChat() {
                    Id = defaultChatId,
                    Version = VersionGenerator.NextVersion(),
                    Title = "The Actual One",
                    CreatedAt = Clocks.SystemClock.Now,
                    CreatorId = adminUserId,
                    IsPublic = true,
                    Owners = {
                        new DbChatOwner() {
                            ChatId = defaultChatId,
                            UserId = adminUserId,
                        },
                    },
                };
                dbContext.Chats.Add(dbChat);
                await dbContext.SaveChangesAsync(cancellationToken);

                for (var id = 0; id < 96; id++) {
                    var dbChatEntry = new DbChatEntry() {
                        ChatId = dbChat.Id,
                        Id = id,
                        CompositeId = DbChatEntry.GetCompositeId(dbChat.Id, id),
                        BeginsAt = Clocks.SystemClock.Now,
                        EndsAt = Clocks.SystemClock.Now,
                        Content = $"Message {id}",
                        ContentType = ChatContentType.Text,
                        CreatorId = adminUserId,
                    };
                    dbContext.Add(dbChatEntry);
                }
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
    }
}

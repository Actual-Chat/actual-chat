using ActualChat.Chat.Db;
using ActualChat.Db;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion.EntityFramework;

namespace ActualChat.Chat.Module
{
    public class ChatDbInitializer : DbInitializer<ChatDbContext>
    {
        public ChatDbInitializer(IServiceProvider services) : base(services) { }

        public override async Task Initialize(CancellationToken cancellationToken)
        {
            await base.Initialize(cancellationToken).ConfigureAwait(false);
            var dependencies = InitializeTasks
                .Where(kv => kv.Key.GetType().Name.StartsWith("Users", StringComparison.Ordinal))
                .Select(kv => kv.Value)
                .ToArray();
            await Task.WhenAll(dependencies).ConfigureAwait(false);

            var dbContextFactory = Services.GetRequiredService<IDbContextFactory<ChatDbContext>>();
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            if (ShouldRecreateDb) {
                // Creating "The Actual One" chat
                var defaultChatId = ChatConstants.DefaultChatId;
                var adminUserId = UserConstants.Admin.UserId;
                var dbChat = new DbChat() {
                    Id = defaultChatId,
                    Version = VersionGenerator.NextVersion(),
                    Title = "The Actual One",
                    CreatedAt = Clocks.SystemClock.Now,
                    IsPublic = true,
                    Owners = {
                        new DbChatOwner() {
                            ChatId = defaultChatId,
                            UserId = adminUserId,
                        },
                    },
                };
                var dbAuthor = new DbAuthor() {
                    Id = UserConstants.Admin.AuthorId,
                    UserId = UserConstants.Admin.UserId,
                    Name = UserConstants.Admin.Name,
                    Picture = UserConstants.Admin.Picture,
                    IsAnonymous = true,
                };
                await dbContext.Authors.AddAsync(dbAuthor, cancellationToken).ConfigureAwait(false);
                await dbContext.Chats.AddAsync(dbChat, cancellationToken).ConfigureAwait(false);
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                var words = new[] { "most", "chat", "actual", "ever", "amazing", "absolutely" };
                for (var id = 0; id < 96; id++) {
                    var dbChatEntry = new DbChatEntry() {
                        ChatId = dbChat.Id,
                        Id = id,
                        CompositeId = DbChatEntry.GetCompositeId(dbChat.Id, id),
                        BeginsAt = Clocks.SystemClock.Now,
                        EndsAt = Clocks.SystemClock.Now,
                        Type = ChatEntryType.Text,
                        Content = GetRandomSentence(30),
                        AuthorId = UserConstants.Admin.AuthorId,
                    };
                    if (id == 0)
                        dbChatEntry.Content = "First";
                    dbContext.Add(dbChatEntry);
                }
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                string GetRandomSentence(int maxLength)
                    => Enumerable
                        .Range(0, Random.Shared.Next(maxLength))
                        .Select(_ => words![Random.Shared.Next(words.Length)])
                        .ToDelimitedString(" ");
            }
        }
    }
}

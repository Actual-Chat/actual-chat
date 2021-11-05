using ActualChat.Chat.Db;
using ActualChat.Db;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ActualChat.Chat.Module;

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
        var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

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
            await dbContext.Chats.AddAsync(dbChat, cancellationToken).ConfigureAwait(false);

            var dbAuthor = new DbChatAuthor() {
                Id = DbChatAuthor.ComposeId(defaultChatId, 1),
                ChatId = defaultChatId,
                LocalId = 1,
                Version = VersionGenerator.NextVersion(),
                Name = UserConstants.Admin.Name,
                Picture = UserConstants.Admin.Picture,
                IsAnonymous = false,
                UserId = adminUserId,
            };
            await dbContext.ChatAuthors.AddAsync(dbAuthor, cancellationToken).ConfigureAwait(false);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var words = new[] { "most", "chat", "actual", "ever", "amazing", "absolutely" };
            for (var id = 0; id < 96; id++) {
                var dbChatEntry = new DbChatEntry() {
                    CompositeId = DbChatEntry.GetCompositeId(dbChat.Id, id),
                    ChatId = dbChat.Id,
                    Id = id,
                    Version = VersionGenerator.NextVersion(),
                    BeginsAt = Clocks.SystemClock.Now,
                    EndsAt = Clocks.SystemClock.Now,
                    Type = ChatEntryType.Text,
                    Content = GetRandomSentence(30),
                    AuthorId = dbAuthor.Id,
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

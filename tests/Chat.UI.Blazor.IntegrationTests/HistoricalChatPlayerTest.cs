using ActualChat.Chat.Db;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.Testing.Host;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public class HistoricalChatPlayerTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    [Fact(Timeout = 60_000)]
    public async Task RewindBackTest()
    {
        var appHost = AppHost;
        await using var tester = appHost.NewBlazorTester(Out);
        var services = tester.ScopedAppServices;
        var account = await tester.SignIn(new User(Constants.User.Admin.Name));
        var clocks = services.Clocks();
        var today = clocks.SystemClock.Now.ToDateTime().Date;
        var yesterday = today.AddDays(-1);
        var entry1BeginsAt = yesterday;
        var entry2BeginsAt = yesterday.AddMinutes(15);
        ChatId chatId;

        var dbContextFactory = services.GetRequiredService<IDbContextFactory<ChatDbContext>>();
        var dbContext = await dbContextFactory.CreateDbContextAsync();
        await using (var _ = dbContext.ConfigureAwait(false)) {
            var dbChat = AddChat(dbContext, yesterday, account.Id);
            chatId = ChatId.Parse(dbChat.Id);
            var dbAuthor = AddAuthor(dbContext, chatId, account.Id);
            var authorId = AuthorId.Parse(dbAuthor.Id);
            long localId = 1;
            AddAudioEntry(dbContext, chatId, authorId, ref localId, entry1BeginsAt, TimeSpan.FromSeconds(20));
            AddAudioEntry(dbContext, chatId, authorId, ref localId, entry2BeginsAt, TimeSpan.FromSeconds(60));
            await dbContext.SaveChangesAsync();
        }

        var player = services.CreateInstance<HistoricalChatPlayer>(chatId);
        // Rewind back along the same audio entry
        var newMoment = await player.GetRewindMoment(entry2BeginsAt.AddSeconds(30), TimeSpan.FromSeconds(-15), default);
        newMoment.Should().Be(entry2BeginsAt.AddSeconds(15).ToMoment());

        // Rewind back to previous entry with a big gap
        newMoment = await player.GetRewindMoment(entry2BeginsAt.AddSeconds(10), TimeSpan.FromSeconds(-15), default);
        newMoment.Should().Be(yesterday.AddSeconds(15).ToMoment());

        // Rewind back to from gap to previous entry
        newMoment = await player.GetRewindMoment(entry2BeginsAt.AddSeconds(-10), TimeSpan.FromSeconds(-15), default);
        newMoment.Should().Be(yesterday.AddSeconds(5).ToMoment());
    }

    private static DbAuthor AddAuthor(ChatDbContext dbContext,  ChatId chatId, UserId userId)
    {
        var dbAuthor = new DbAuthor {
            Id = AuthorId.New(chatId, 1).Value,
            ChatId = chatId.Value,
            LocalId = 1,
            Version = 1,
            IsAnonymous = false,
            UserId = userId.Value,
        };
        dbContext.Authors.Add(dbAuthor);
        return dbAuthor;
    }

    private static DbChat AddChat(ChatDbContext dbContext, DateTime createdAt, UserId ownerUserId)
    {
        var chatId = ChatId.Parse("testChat");
        var dbChat = new DbChat {
            Id = chatId.Value,
            Version = 1,
            Title = "Test chat",
            CreatedAt = createdAt,
            IsPublic = true,
        };
        dbContext.Chats.Add(dbChat);
        return dbChat;
    }

    private static void AddAudioEntry(
        ChatDbContext dbContext,
        ChatId chatId,
        AuthorId authorId,
        ref long localId,
        DateTime beginsAt,
        TimeSpan duration)
    {
        var id = AudioEntryId.New(chatId, localId);
        var audioEntry = new DbChatEntry {
            Id = id.Value,
            ChatId = id.ChatId.Value,
            AuthorId = authorId.Value,
            Kind = id.Kind,
            LocalId = id.LocalId,
            Version = 1,
            BeginsAt = beginsAt,
            EndsAt = beginsAt.Add(duration),
        };
        dbContext.Add(audioEntry);
        localId++;
    }
}

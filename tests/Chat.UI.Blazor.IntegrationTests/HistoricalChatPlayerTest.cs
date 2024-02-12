using ActualChat.App.Server;
using ActualChat.Chat.Db;
using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Testing.Host;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection)), Trait("Category", nameof(ChatUICollection))]
public class HistoricalChatPlayerTest(AppHostFixture fixture, ITestOutputHelper @out)
{
    private TestAppHost Host => fixture.Host;
    private ITestOutputHelper Out { get; } = fixture.Host.SetOutput(@out);

    [Fact(Timeout = 60_000)]
    public async Task RewindBackTest()
    {
        var appHost = Host;
        await using var tester = appHost.NewBlazorTester();
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
            chatId = new ChatId(dbChat.Id);
            var dbAuthor = AddAuthor(dbContext, chatId, account.Id);
            var authorId = new AuthorId(dbAuthor.Id);
            long localId = 1;
            AddAudioEntry(dbContext, chatId, authorId, ref localId, entry1BeginsAt, TimeSpan.FromSeconds(20));
            AddAudioEntry(dbContext, chatId, authorId, ref localId, entry2BeginsAt, TimeSpan.FromSeconds(60));
            await dbContext.SaveChangesAsync();
        }

        var player = services.Activate<HistoricalChatPlayer>(chatId);
        // Rewind back along same audio entry
        var newMoment = await player.GetRewindMoment(entry2BeginsAt.AddSeconds(30), TimeSpan.FromSeconds(-15), default);
        newMoment.Should().Be(entry2BeginsAt.AddSeconds(15).ToMoment());

        // Rewind back to previous entry with big gap
        newMoment = await player.GetRewindMoment(entry2BeginsAt.AddSeconds(10), TimeSpan.FromSeconds(-15), default);
        newMoment.Should().Be(yesterday.AddSeconds(15).ToMoment());

        // Rewind back to from gap to previous entry
        newMoment = await player.GetRewindMoment(entry2BeginsAt.AddSeconds(-10), TimeSpan.FromSeconds(-15), default);
        newMoment.Should().Be(yesterday.AddSeconds(5).ToMoment());
    }

    private static DbAuthor AddAuthor(ChatDbContext dbContext,  ChatId chatId, UserId userId)
    {
        var dbAuthor = new DbAuthor {
            Id = new AuthorId(chatId, 1, AssumeValid.Option),
            ChatId = chatId,
            LocalId = 1,
            Version = 1,
            IsAnonymous = false,
            UserId = userId,
        };
        dbContext.Authors.Add(dbAuthor);
        return dbAuthor;
    }

    private static DbChat AddChat(ChatDbContext dbContext, DateTime сreatedAt, UserId ownerUserId)
    {
        var chatId = new ChatId("testchat");
        var dbChat = new DbChat {
            Id = chatId,
            Version = 1,
            Title = "Test chat",
            CreatedAt = сreatedAt,
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
        var id = new ChatEntryId(chatId, ChatEntryKind.Audio, localId, AssumeValid.Option);
        var audioEntry = new DbChatEntry {
            Id = id,
            ChatId = id.ChatId,
            AuthorId = authorId,
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

using ActualChat.App.Server;
using ActualChat.Chat.Db;
using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Testing.Host;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

public class HistoricalChatPlayerTest : AppHostTestBase
{
    private BlazorTester _tester = null!;
    private AppHost _appHost = null!;
    private User _user = null!;

    public HistoricalChatPlayerTest(ITestOutputHelper @out) : base(@out) { }

    public override async Task InitializeAsync()
    {
        _appHost = await NewAppHost();
        _tester = _appHost.NewBlazorTester();
        _user = await _tester.SignIn(new User(UserConstants.Admin.Name), default);
    }

    public override async Task DisposeAsync()
    {
        await _tester.DisposeAsync().AsTask();
        _appHost.Dispose();
    }

    [Fact]
    public async Task RewindBackTest()
    {
        var services = _tester.ScopedAppServices;

        var clocks = services.GetRequiredService<MomentClockSet>();
        var today = clocks.SystemClock.Now.ToDateTime().Date;
        var yesterday = today.AddDays(-1);
        var entry1BeginsAt = yesterday;
        var entry2BeginsAt = yesterday.AddMinutes(15);
        string chatId;

        var dbContextFactory = services.GetRequiredService<IDbContextFactory<ChatDbContext>>();
        var dbContext = await dbContextFactory.CreateDbContextAsync().ConfigureAwait(false);
        await using (var _ = dbContext.ConfigureAwait(false)) {
            var dbChat = AddChat(dbContext, yesterday, _user.Id);
            chatId = dbChat.Id;
            var dbAuthor = AddAuthor(dbContext, dbChat.Id, _user.Id);
            var authorId = dbAuthor.Id;
            long entryId = 1;
            AddAudioEntry(dbContext, dbChat.Id, authorId, ref entryId, entry1BeginsAt, TimeSpan.FromSeconds(20));
            AddAudioEntry(dbContext, dbChat.Id, authorId, ref entryId, entry2BeginsAt, TimeSpan.FromSeconds(60));
            await dbContext.SaveChangesAsync().ConfigureAwait(false);
        }

        var player = services.Activate<HistoricalChatPlayer>((Symbol)chatId);
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

    private static DbAuthor AddAuthor(ChatDbContext dbContext,  string chatId, string userId)
    {
        var dbAuthor = new DbAuthor {
            Id = DbAuthor.ComposeId(chatId, 1),
            ChatId = chatId,
            LocalId = 1,
            Version = 1,
            IsAnonymous = false,
            UserId = userId,
        };
        dbContext.Authors.Add(dbAuthor);
        return dbAuthor;
    }

    private static DbChat AddChat(ChatDbContext dbContext, DateTime сreatedAt, string ownerUserId)
    {
        const string chatId = "test-chat";
        var dbChat = new DbChat {
            Id = chatId,
            Version = 1,
            Title = "Test chat",
            CreatedAt = сreatedAt,
            IsPublic = true,
            Owners = {
                new DbChatOwner {
                    DbChatId = chatId,
                    DbUserId = ownerUserId,
                },
            },
        };
        dbContext.Chats.Add(dbChat);
        return dbChat;
    }

    private static void AddAudioEntry(ChatDbContext dbContext, string chatId, string authorId, ref long entryId, DateTime beginsAt, TimeSpan duration)
    {
        var audioEntry = new DbChatEntry {
            Id = DbChatEntry.ComposeId(chatId, ChatEntryKind.Audio, entryId),
            ChatId = chatId,
            AuthorId = authorId,
            Kind = ChatEntryKind.Audio,
            LocalId = entryId,
            Version = 1,
            BeginsAt = beginsAt,
            EndsAt = beginsAt.Add(duration),
        };
        dbContext.Add(audioEntry);
        entryId++;
    }
}

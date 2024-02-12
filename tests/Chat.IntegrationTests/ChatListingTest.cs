using ActualChat.Performance;
using ActualChat.Testing.Assertion;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

// TODO: merge with ChatOperationsTest
[Collection(nameof(ChatCollection)), Trait("Category", nameof(ChatCollection))]
public class ChatListingTest(AppHostFixture fixture, ITestOutputHelper @out)
    : AppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester _tester = null!;

    public override Task InitializeAsync()
    {
        Tracer.Default = Out.NewTracer();
        _tester = AppHost.NewWebClientTester(Out);
        FluentAssertions.Formatting.Formatter.AddFormatter(new UserFormatter());
        return Task.CompletedTask;
    }

    public override async Task DisposeAsync()
    {
        Tracer.Default = Tracer.None;
        foreach (var formatter in FluentAssertions.Formatting.Formatter.Formatters.OfType<UserFormatter>().ToList())
            FluentAssertions.Formatting.Formatter.RemoveFormatter(formatter);
        await _tester.DisposeAsync();
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 10)]
    [InlineData(10, 3)]
    [InlineData(10, 10)]
    public async Task ShouldListAllChats(int chatCount, int limit)
    {
        // arrange
        var chatsBackend = AppHost.Services.GetRequiredService<IChatsBackend>();
        var commander = AppHost.Services.Commander();
        var clock = AppHost.Services.Clocks().ServerClock;
        var now = clock.Now;
        var allExpectedIds = new List<ChatId>();
        await _tester.SignInAsBob();
        for (int i = 0; i < chatCount; i++) {
            var diff = new ChatDiff {
                Title = $"Chat{i}",
                IsPublic = i % 2 == 0,
                Kind = ChatKind.Group,
            };
            var cmd = new Chats_Change(_tester.Session, ChatId.None, null, new () { Create = diff, });
            var (chatId, _) = await commander.Call(cmd);
            allExpectedIds.Add(chatId);
        }

        // act
        await foreach (var chats in chatsBackend.Batches(now, ChatId.None, limit, CancellationToken.None)) {
            chats.Should().NotBeEmpty();
            var chatIds = chats
#pragma warning disable CA1310
                .Where(c => c.Title.StartsWith("Chat"))
#pragma warning restore CA1310
                .Select(x => x.Id)
                .ToList();

            // assert
            allExpectedIds[..chatIds.Count].Should().Equal(chatIds);
            allExpectedIds = allExpectedIds[chatIds.Count..];
        }

        // assert
        allExpectedIds.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 10)]
    [InlineData(10, 3)]
    [InlineData(10, 10)]
    public async Task ShouldReturnEmpty(int chatCount, int limit)
    {
        // arrange
        var chatsBackend = AppHost.Services.GetRequiredService<IChatsBackend>();
        var commander = AppHost.Services.Commander();
        var allChats = new List<Chat>();
        await _tester.SignInAsBob();
        for (int i = 0; i < chatCount; i++) {
            var diff = new ChatDiff {
                Title = $"Chat{i}",
                IsPublic = i % 2 == 0,
                Kind = ChatKind.Group,
            };
            var cmd = new Chats_Change(_tester.Session, ChatId.None, null, new () { Create = diff, });
            var chat = await commander.Call(cmd);
            allChats.Add(chat);
        }
        var minCreatedAt = allChats[^1].CreatedAt;
        var lastChatId = allChats[^1].Id;

        // act
        var batches = await chatsBackend.Batches(minCreatedAt, lastChatId, limit, CancellationToken.None).ToListAsync();

        // assert
        batches.Should().BeEmpty();
    }
}

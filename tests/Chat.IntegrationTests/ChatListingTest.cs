using ActualChat.Performance;
using ActualChat.Testing.Assertion;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class ChatListingTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester _tester = null!;

    protected override Task InitializeAsync()
    {
        Tracer.Default = Out.NewTracer();
        _tester = AppHost.NewWebClientTester(Out);
        FluentAssertions.Formatting.Formatter.AddFormatter(new UserFormatter());
        return Task.CompletedTask;
    }

    protected override async Task DisposeAsync()
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
        await foreach (var chats in chatsBackend.Batch(now, ChatId.None, limit, CancellationToken.None)) {
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
        await _tester.SignInAsBob();
        var allChats = await CreateChats(chatCount);
        var minCreatedAt = allChats[^1].CreatedAt;
        var lastChatId = allChats[^1].Id;

        // act
        var batches = await chatsBackend.Batch(minCreatedAt, lastChatId, limit, CancellationToken.None).ToListAsync();

        // assert
        batches.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 2)]
    [InlineData(10, 3)]
    [InlineData(10, 10)]
    public async Task ShouldListChanged(int chatCount, int limit)
    {
        // arrange
        var chatsBackend = AppHost.Services.GetRequiredService<IChatsBackend>();
        var lastChanged = await chatsBackend.GetLastChanged(CancellationToken.None);
        await _tester.SignInAsBob();
        var created = await CreateChats(chatCount);

        // act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;
        var retrieved = await chatsBackend.BatchChanged(lastChanged?.Version ?? 0,
                long.MaxValue,
                lastChanged?.Id ?? ChatId.None,
                limit,
                cancellationToken)
            .ToApiArrayAsync(cancellationToken)
            .Flatten();

        // assert
        retrieved.Select(x => x.Id).Should().Contain(created.Select(x => x.Id));
    }

    private async Task<Chat[]> CreateChats(int count)
    {
        var chats = new Chat[count];
        for (int i = 0; i < count; i++) {
            var (chatId, _) = await _tester.CreateChat(i % 2 == 0, $"Chat {i}");
            chats[i] = await _tester.Chats.Get(_tester.Session, chatId, CancellationToken.None).Require();
        }

        return chats;
    }
}

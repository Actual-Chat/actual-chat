using ActualChat.Testing.Assertion;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class ChatListingTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private WebClientTester Tester { get; } = fixture.AppHost.NewWebClientTester(@out);
    private IChatsBackend ChatsBackend { get; } = fixture.AppHost.Services.GetRequiredService<IChatsBackend>();

    protected override Task InitializeAsync()
    {
        FluentAssertions.Formatting.Formatter.AddFormatter(new UserFormatter());
        return Task.CompletedTask;
    }

    protected override async Task DisposeAsync()
    {
        foreach (var formatter in FluentAssertions.Formatting.Formatter.Formatters.OfType<UserFormatter>().ToList())
            FluentAssertions.Formatting.Formatter.RemoveFormatter(formatter);
        await Tester.DisposeAsync();
    }

    [Theory]
    [InlineData(150, 27)]
    [InlineData(30, 15)]
    public async Task ShouldListAllChats(int chatCount, int batchSize)
    {
        // arrange
        var now = Clocks.ServerClock.Now;
        await Tester.SignInAsBob();
        var created = await CreateChats(chatCount);

        // act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;
        var retrieved = await ChatsBackend.Batch(now, ChatId.None, batchSize, cancellationToken)
            .ToApiArrayAsync(cancellationToken)
            .Flatten();

        // assert
        retrieved.Select(x => x.Title).Should().Contain(created.Select(x => x.Title));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 10)]
    [InlineData(10, 3)]
    [InlineData(10, 10)]
    public async Task ShouldReturnEmpty(int chatCount, int limit)
    {
        // arrange
        await Tester.SignInAsBob();
        var allChats = await CreateChats(chatCount);
        var minCreatedAt = allChats[^1].CreatedAt;
        var lastChatId = allChats[^1].Id;

        // act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;
        var batches = await ChatsBackend.Batch(minCreatedAt, lastChatId, limit, cancellationToken).ToListAsync(cancellationToken);

        // assert
        batches.Should().BeEmpty();
    }

    [Theory]
    [InlineData(150, 27)]
    [InlineData(30, 15)]
    public async Task ShouldListChanged(int chatCount, int limit)
    {
        // arrange
        var lastChanged = await ChatsBackend.GetLastChanged(CancellationToken.None);
        await Tester.SignInAsBob();
        var created = await CreateChats(chatCount);

        // act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;
        var retrieved = await ChatsBackend.BatchChanged(lastChanged?.Version ?? 0,
                long.MaxValue,
                lastChanged?.Id ?? ChatId.None,
                limit,
                cancellationToken)
            .ToApiArrayAsync(cancellationToken)
            .Flatten();

        // assert
        retrieved.Select(x => x.Title).Should().Contain(created.Select(x => x.Title));
    }

    private async Task<Chat[]> CreateChats(int count)
    {
        var chats = new Chat[count];
        for (int i = 0; i < count; i++) {
            var (chatId, _) = await Tester.CreateChat(i % 2 == 0, UniqueNames.Chat(i));
            chats[i] = await Tester.Chats.Get(Tester.Session, chatId, CancellationToken.None).Require();
        }

        return chats;
    }
}

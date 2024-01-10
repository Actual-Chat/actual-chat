using ActualChat.App.Server;
using ActualChat.Performance;
using ActualChat.Testing.Assertion;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

// TODO: merge with ChatOperationsTest
public class ChatListingTest(ITestOutputHelper @out) : AppHostTestBase(@out)
{
    private WebClientTester _tester = null!;
    private IChatsBackend _sut = null!;
    private AppHost _appHost = null!;
    private ICommander _commander = null!;

    public override async Task InitializeAsync()
    {
        Tracer.Default = Out.NewTracer();
        _appHost = await NewAppHost();
        _tester = _appHost.NewWebClientTester();
        _sut = _appHost.Services.GetRequiredService<IChatsBackend>();
        _commander = _appHost.Services.Commander();
        FluentAssertions.Formatting.Formatter.AddFormatter(new UserFormatter());
    }

    public override async Task DisposeAsync()
    {
        Tracer.Default = Tracer.None;
        foreach (var formatter in FluentAssertions.Formatting.Formatter.Formatters.OfType<UserFormatter>().ToList())
            FluentAssertions.Formatting.Formatter.RemoveFormatter(formatter);
        await _tester.DisposeAsync().AsTask();
        _appHost.Dispose();
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 10)]
    [InlineData(10, 3)]
    [InlineData(10, 10)]
    public async Task ShouldListAllChats(int chatCount, int limit)
    {
        // arrange
        await _tester.SignInAsBob();
        var allExpectedIds = new List<ChatId>();
        for (int i = 0; i < chatCount; i++) {
            var diff = new ChatDiff {
                Title = $"Chat{i}",
                IsPublic = i % 2 == 0,
                Kind = ChatKind.Group,
            };
            var cmd = new Chats_Change(_tester.Session, ChatId.None, null, new () { Create = diff, });
            var (chatId, _) = await _commander.Call(cmd);
            allExpectedIds.Add(chatId);
        }

        // act
        await foreach (var chats in _sut.Batches(Moment.MinValue, ChatId.None, limit, CancellationToken.None)) {
            chats.Should().NotBeEmpty();
            var chatIds = chats.Select(x => x.Id).ToList();

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
        await _tester.SignInAsBob();
        var allChats = new List<Chat>();
        for (int i = 0; i < chatCount; i++) {
            var diff = new ChatDiff {
                Title = $"Chat{i}",
                IsPublic = i % 2 == 0,
                Kind = ChatKind.Group,
            };
            var cmd = new Chats_Change(_tester.Session, ChatId.None, null, new () { Create = diff, });
            var chat = await _commander.Call(cmd);
            allChats.Add(chat);
        }
        var minCreatedAt = allChats[^1].CreatedAt;
        var lastChatId = allChats[^1].Id;

        // act
        var batches = await _sut.Batches(minCreatedAt, lastChatId, limit, CancellationToken.None).ToListAsync();

        // assert
        batches.Should().BeEmpty();
    }
}

using ActualChat.Chat.Flows;
using ActualChat.Queues;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(ChatCollection))]
public class StreamingEntryFixupTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly TimeSpan StaleDelta = Constants.Chat.StreamingEntryFixupDelay + TimeSpan.FromMinutes(2);
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task StaleEmptyStreamingEntryIsRemovedByFixupFlow()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var chatId = await CreateUserChat(tester);
        var clocks = tester.AppServices.Clocks();
        var streaming = await tester.CreateStreamingEntry(
            chatId,
            Languages.English,
            beginsAt: clocks.SystemClock.Now - StaleDelta,
            content: "");

        // act
        await RunFixupFlow(chatId, tester);

        // assert
        await ComputedTest.When(async ct => {
            var current = await tester.Chats.GetEntry(tester.Session, streaming.ChatEntrySlim.Id, ct);
            current.Should().Match<ChatEntry?>(e => e == null || e.IsRemoved);
        }, WaitTimeout);
    }

    [Fact]
    public async Task StaleStreamingEntryWithTextIsFinalizedByFixupFlow()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var chatId = await CreateUserChat(tester);
        var clocks = tester.AppServices.Clocks();
        var streaming = await tester.CreateStreamingEntry(
            chatId,
            Languages.English,
            beginsAt: clocks.SystemClock.Now - StaleDelta,
            content: "partial transcript");

        // act
        await RunFixupFlow(chatId, tester);

        // assert
        await ComputedTest.When(async ct => {
            var current = await tester.Chats.GetEntry(tester.Session, streaming.ChatEntrySlim.Id, ct);
            current.Should().NotBeNull();
            current!.IsRemoved.Should().BeFalse();
            current.IsContentStreaming.Should().BeFalse(because: "fix-up flow must close stale streaming entries with text");
            current.EndsAt.Should().NotBeNull();
            current.Content.Should().Be("partial transcript");
        }, WaitTimeout);
    }

    [Fact]
    public async Task FreshStreamingEntryIsLeftAloneByFixupFlow()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var chatId = await CreateUserChat(tester);
        var streaming = await tester.CreateStreamingEntry(chatId, Languages.English, content: "fresh");

        // act
        await RunFixupFlow(chatId, tester);

        // assert
        var current = await tester.Chats.GetEntry(tester.Session, streaming.ChatEntrySlim.Id, CancellationToken.None);
        current.Should().NotBeNull();
        current!.IsRemoved.Should().BeFalse();
        current.IsContentStreaming.Should().BeTrue();
    }

    [Fact]
    public async Task FixupFlowRescansPastAFreshStreamingEntry()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var chatId = await CreateUserChat(tester);
        var clocks = tester.AppServices.Clocks();
        var fresh = await tester.CreateStreamingEntry(chatId, Languages.English, content: "fresh");
        await RunFixupFlow(chatId, tester);

        // act
        // The fresh entry pins the flow's cursor, so this later one is only ever
        // reached if the next run rescans from the pinned position rather than past it.
        var stale = await tester.CreateStreamingEntry(
            chatId,
            Languages.English,
            beginsAt: clocks.SystemClock.Now - StaleDelta,
            content: "stale after fresh");
        await RunFixupFlow(chatId, tester);

        // assert
        await ComputedTest.When(async ct => {
            var current = await tester.Chats.GetEntry(tester.Session, stale.ChatEntrySlim.Id, ct);
            current.Should().NotBeNull();
            current!.IsContentStreaming.Should().BeFalse();
        }, WaitTimeout);
        var freshNow = await tester.Chats.GetEntry(tester.Session, fresh.ChatEntrySlim.Id, CancellationToken.None);
        freshNow.Should().NotBeNull();
        freshNow!.IsContentStreaming.Should().BeTrue();
    }

    [Fact]
    public async Task RemoveStreamingEntrySucceeds()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        var chatId = await CreateUserChat(tester);
        var streaming = await tester.CreateStreamingEntry(chatId, Languages.English, content: "still streaming");
        streaming.ChatEntrySlim.IsContentStreaming.Should().BeTrue();

        // act
        var remove = () => tester.RemoveTextEntry(streaming.ChatEntrySlim.Id);

        // assert
        await remove.Should().NotThrowAsync();
        await ComputedTest.When(async ct => {
            var current = await tester.Chats.GetEntry(tester.Session, streaming.ChatEntrySlim.Id, ct);
            current.Should().Match<ChatEntry?>(e => e == null || e.IsRemoved);
        }, WaitTimeout);
    }

    // Private methods

    private static async Task<ChatId> CreateUserChat(WebClientTester tester)
    {
        await tester.SignInAsUniqueAlice();
        var chat = await tester.Commander.Call(new Chats_Change(tester.Session, default, null, new() {
            Create = new ChatDiff {
                Title = "Streaming-fixup test",
                Kind = ChatKind.Group,
                IsPublic = false,
            },
        }));
        // Posting a regular text entry ensures the author exists —
        // CreateStreamingEntry calls GetOwnAuthor.Require() and would otherwise fail.
        await tester.CreateTextEntry(chat.Id, "seed");
        return chat.Id;
    }

    private async Task RunFixupFlow(ChatId chatId, IWebTester tester)
    {
        // Immediate (no-delay) resume — bypasses the production scheduling delay.
        await FlowHub.NewResumeEvent<ChatEntryFixupFlow>(chatId.Value)
            .WithDelayQuanta(TimeSpan.Zero)
            .Schedule();
        await tester.AppServices.Queues().WhenProcessing();
    }
}

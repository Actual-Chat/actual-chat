using ActualChat.Chat.Flows;
using ActualChat.Flows;
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
        await RunFixupFlow(tester);

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
        await RunFixupFlow(tester);

        // assert
        await ComputedTest.When(async ct => {
            var current = await tester.Chats.GetEntry(tester.Session, streaming.ChatEntrySlim.Id, ct);
            current.Should().NotBeNull();
            current!.IsRemoved.Should().BeFalse();
            current.IsContentStreaming.Should()
                .BeFalse(because: "fix-up flow must close stale streaming entries with text");
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
        await RunFixupFlow(tester);

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
        await RunFixupFlow(tester);

        // act
        // A fresh entry must not hold back a later stale one — the predecessor flow
        // tracked a per-chat cursor that the fresh entry pinned, stalling everything behind it.
        var stale = await tester.CreateStreamingEntry(
            chatId,
            Languages.English,
            beginsAt: clocks.SystemClock.Now - StaleDelta,
            content: "stale after fresh");
        await RunFixupFlow(tester);

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
    public async Task FixupFlowKeepsRunningWithoutAnExternalKick()
    {
        // arrange
        // The predecessor woke up only from a per-chat entry-creation event, so one lost event
        // starved that chat's fix-up forever. These two traits are what make that unreachable.
        typeof(StreamingEntryFixupFlow).Should().BeAssignableTo<IMasterFlow>();
        typeof(StreamingEntryFixupFlow).Should().BeAssignableTo<PeriodicFlow>();

        await using var tester = AppHost.NewWebClientTester(Out);
        var chatId = await CreateUserChat(tester);
        var clocks = tester.AppServices.Clocks();
        await tester.CreateStreamingEntry(
            chatId,
            Languages.English,
            beginsAt: clocks.SystemClock.Now - StaleDelta,
            content: "needs a run to be closed");

        // act
        await RunFixupFlow(tester);

        // assert
        var flow = await FlowHub.TryGet<StreamingEntryFixupFlow>("");
        flow.Should().NotBeNull();
        flow!.RunCount.Should().BePositive(because: "the stale entry above must have triggered a run");
        flow.UntypedResult.Should().BeNull(because: "a completed flow would never run again");
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

    private async Task RunFixupFlow(IWebTester tester)
    {
        // Immediate (no-delay) resume of the master flow — bypasses its run interval.
        await FlowHub.NewResumeEvent<StreamingEntryFixupFlow>("")
            .WithDelayQuanta(TimeSpan.Zero)
            .Schedule();
        await tester.AppServices.Queues().WhenProcessing();
    }
}
